using System.Text;
using System.Text.Json;
using aspire_react.Server.Application.Common.Validation;
using aspire_react.Server.Application.Components.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Web.ExceptionHandlers;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [FIX-GUID-EMPTY phần 2 — 2026-10-20] Hai phần:
/// <para>
/// (A) <b>Lưới an toàn toàn cục</b> <see cref="ForeignKeyViolationExceptionHandler"/>: Postgres 23503
/// "insert or update … violates foreign key" ⇒ 400 <c>REFERENCE_NOT_FOUND</c>; "update or delete … violates
/// foreign key" ⇒ 409 <c>RESOURCE_IN_USE</c>. Test tạo **lỗi FK THẬT trên Postgres** (InMemory không thực
/// thi FK) rồi đưa exception thật vào handler — nên 2 nhánh được kiểm bằng SQLSTATE thật, không phải chuỗi
/// tự chế. Có test âm: SQLSTATE khác (23505/23514) KHÔNG bị handler này nuốt (giữ xử lý riêng hiện có).
/// </para>
/// <para>
/// (B) <b>Quy ước FK tùy chọn</b> trong lệnh Update: vắng = giữ; <c>Guid.Empty</c> = xóa liên kết;
/// id không tồn tại ⇒ 400 <c>RESOURCE_NOT_FOUND</c>.
/// </para>
/// </summary>
public class ForeignKeyViolationHandlerTests
{
    public const string ConnectionStringEnv = "MIRATS_TEST_POSTGRES";

    private static readonly Guid ActorId = Guid.NewGuid();

    // ==================== (A) lưới an toàn 23503 ====================

    [ThrowawayDbFact]
    public async Task Insert_With_Missing_Parent_OnRealPostgres_Maps_To_400_ReferenceNotFound()
    {
        await using var db = await CreatePostgresContextAsync();

        // assets.LocationId là FK tới locations (OnDelete SetNull), nhưng id KHÔNG tồn tại vẫn vi phạm FK.
        var asset = new Asset
        {
            AssetTag = UniqueTag("GUIDFK"),
            Name = "fk insert probe",
            Status = AssetStatus.Pending,
            LocationId = Guid.NewGuid()
        };
        db.Assets.Add(asset);
        var exception = await CaptureAsync(() => db.SaveChangesAsync());

        var (handler, http) = CreateHandler();
        var handled = await handler.TryHandleAsync(http, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, http.Response.StatusCode);
        var payload = await ReadJsonAsync(http);
        Assert.Equal("error", payload.GetProperty("status").GetString());
        Assert.Equal(ForeignKeyViolationExceptionHandler.ReferenceNotFoundCode,
            payload.GetProperty("error_code").GetString());
        // Log warning kèm tên constraint: response cũng phơi tên constraint để chẩn đoán.
        Assert.Contains("locations", payload.GetProperty("constraint").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("assets", payload.GetProperty("table").GetString());
    }

    [ThrowawayDbFact]
    public async Task Delete_Parent_Still_Referenced_OnRealPostgres_Maps_To_409_ResourceInUse()
    {
        // locations.ParentId là FK Restrict (self-reference) — xóa cha khi còn con ⇒ 23503 nhánh "delete".
        // Dùng CONTEXT RIÊNG cho từng bước để tránh EF fixup qua navigation `Parent.Children` (nếu để chung
        // context, EF có thể null hóa/xóa theo graph và test sẽ không chạm ràng buộc thật của DB).
        Guid parentId;
        Guid childId;
        await using (var seed = await CreatePostgresContextAsync())
        {
            var parent = new Location { Name = UniqueTag("GUIDFK-PARENT") };
            seed.Locations.Add(parent);
            await seed.SaveChangesAsync();
            parentId = parent.Id;

            var child = new Location { Name = UniqueTag("GUIDFK-CHILD"), ParentId = parentId };
            seed.Locations.Add(child);
            await seed.SaveChangesAsync();
            childId = child.Id;
        }

        await using (var verify = await CreatePostgresContextAsync())
        {
            Assert.Equal(parentId, (await verify.Locations.AsNoTracking().SingleAsync(l => l.Id == childId)).ParentId);
        }

        await using var db = await CreatePostgresContextAsync();
        db.Locations.Remove(new Location { Id = parentId });
        var exception = await CaptureAsync(() => db.SaveChangesAsync());

        var (handler, http) = CreateHandler();
        var handled = await handler.TryHandleAsync(http, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, http.Response.StatusCode);
        var payload = await ReadJsonAsync(http);
        Assert.Equal("error", payload.GetProperty("status").GetString());
        Assert.Equal(ForeignKeyViolationExceptionHandler.ResourceInUseCode,
            payload.GetProperty("error_code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("constraint").GetString()));

        // dọn fixture (con trước, cha sau)
        await using var cleanup = await CreatePostgresContextAsync();
        cleanup.Locations.Remove(new Location { Id = childId });
        await cleanup.SaveChangesAsync();
        cleanup.Locations.Remove(new Location { Id = parentId });
        await cleanup.SaveChangesAsync();
    }

    [Fact]
    public async Task Other_SqlStates_Are_Not_Handled_So_Existing_Mappings_Keep_Working()
    {
        var (handler, http) = CreateHandler();

        var uniqueViolation = new PostgresException(
            "duplicate key value violates unique constraint \"IX_x\"", "ERROR", "ERROR",
            PostgresErrorCodes.UniqueViolation);
        Assert.False(await handler.TryHandleAsync(http, new DbUpdateException("dup", uniqueViolation), CancellationToken.None));

        var checkViolation = new PostgresException(
            "new row for relation violates check constraint", "ERROR", "ERROR",
            PostgresErrorCodes.CheckViolation);
        Assert.False(await handler.TryHandleAsync(http, new DbUpdateException("check", checkViolation), CancellationToken.None));

        Assert.False(await handler.TryHandleAsync(http, new InvalidOperationException("boom"), CancellationToken.None));
    }

    [Fact]
    public async Task ForeignKeyViolation_Handler_Ignores_When_Response_Already_Started()
    {
        var (handler, http) = CreateHandler();
        http.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(new StartedResponseFeature());

        var pg = new PostgresException(
            "insert or update on table \"assets\" violates foreign key constraint \"FK_assets_locations_LocationId\"",
            "ERROR", "ERROR", PostgresErrorCodes.ForeignKeyViolation,
            constraintName: "FK_assets_locations_LocationId", tableName: "assets");

        Assert.False(await handler.TryHandleAsync(http, new DbUpdateException("fk", pg), CancellationToken.None));
    }

    // ==================== (B) quy ước FK tùy chọn ====================

    [Fact]
    public void OptionalForeignKey_Convention_Three_States()
    {
        // vắng (null) → null (handler hiểu là "giữ nguyên" vì chỉ gán khi request khác null)
        Assert.Null(OptionalForeignKey.Cleared(null));
        // Guid.Empty → null (XÓA liên kết)
        Assert.Null(OptionalForeignKey.Cleared(Guid.Empty));
        Assert.True(OptionalForeignKey.IsClearRequested(Guid.Empty));
        Assert.False(OptionalForeignKey.IsClearRequested(null));
        Assert.False(OptionalForeignKey.IsClearRequested(Guid.NewGuid()));
        // id thật → giữ nguyên
        var id = Guid.NewGuid();
        Assert.Equal(id, OptionalForeignKey.Cleared(id));
        Assert.False(string.IsNullOrWhiteSpace(OptionalForeignKey.NotFoundMessage("supplierId")));
    }

    [Fact]
    public async Task UpdateComponent_GuidEmpty_Clears_Supplier_Link()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(UpdateComponent_GuidEmpty_Clears_Supplier_Link));
        var supplier = new Supplier { Code = "SUP-CLR", Name = "NCC để xóa" };
        ctx.Suppliers.Add(supplier);
        await ctx.SaveChangesAsync();
        var component = new Component { Name = "Linh kiện", Qty = 3, SupplierId = supplier.Id };
        ctx.Components.Add(component);
        await ctx.SaveChangesAsync();

        var handler = new UpdateComponentCommandHandler(
            ctx, new TestHelpers.SuperUserScope(), TestHelpers.CreateActionLogService(ctx, ActorId));

        var result = await handler.Handle(
            new UpdateComponentCommand(component.Id, null, null, Guid.Empty, null, null, null, null,
                null, null, null, null, null, null, ActorId),
            CancellationToken.None);

        Assert.True(result.Success, result.Message);
        var reloaded = await ctx.Components.AsNoTracking().SingleAsync(c => c.Id == component.Id);
        Assert.Null(reloaded.SupplierId);
    }

    [Fact]
    public async Task UpdateComponent_Unknown_Supplier_Returns_400_ReferenceNotFound()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(UpdateComponent_Unknown_Supplier_Returns_400_ReferenceNotFound));
        var component = new Component { Name = "Linh kiện 2", Qty = 1 };
        ctx.Components.Add(component);
        await ctx.SaveChangesAsync();

        var handler = new UpdateComponentCommandHandler(
            ctx, new TestHelpers.SuperUserScope(), TestHelpers.CreateActionLogService(ctx, ActorId));

        var result = await handler.Handle(
            new UpdateComponentCommand(component.Id, null, null, Guid.NewGuid(), null, null, null, null,
                null, null, null, null, null, null, ActorId),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("RESOURCE_NOT_FOUND", result.ErrorCode);
    }

    // ==================== hạ tầng ====================

    private static async Task<AppDbContext> CreatePostgresContextAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnv)?.Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                $"Thiếu {ConnectionStringEnv} (connection string tới DB TẠM trong container Postgres RIÊNG).");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("aspire-react.Server"))
            .Options;

        var db = new AppDbContext(options, new TestHelpers.SuperUserScope());
        await ThrowawayDbMigrator.MigrateOnceAsync(db, connectionString);
        return db;
    }

    private static (ForeignKeyViolationExceptionHandler Handler, DefaultHttpContext Http) CreateHandler()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();

        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        http.Response.Body = new MemoryStream();

        var handler = new ForeignKeyViolationExceptionHandler(
            NullLogger<ForeignKeyViolationExceptionHandler>.Instance);
        return (handler, http);
    }

    private static async Task<Exception> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            return exception;
        }

        throw new Xunit.Sdk.XunitException("Mong đợi một exception từ Postgres nhưng SaveChanges đã thành công.");
    }

    private static async Task<JsonElement> ReadJsonAsync(DefaultHttpContext http)
    {
        http.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(http.Response.Body, Encoding.UTF8).ReadToEndAsync();
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private static string UniqueTag(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    /// <summary>HttpResponseFeature giả lập "response đã bắt đầu gửi".</summary>
    private sealed class StartedResponseFeature : Microsoft.AspNetCore.Http.Features.IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted => true;

        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) { }
    }
}
