using aspire_react.Server.Application.Assets.Queries;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [BUG-Q] Hồi quy cho <c>GetAssetByIdQueryHandler</c> (chi tiết thiết bị) — lỗi thật trên Postgres:
/// <c>GET /api/v1/assets/{id}</c> trả <b>500 cho MỌI thiết bị đang có <c>CurrentAssignment</c></b>
/// (User / Department / SystemPosition), trong khi thiết bị không gắn ai trả 200.
/// <para>
/// Nguyên nhân: <c>Select(x =&gt; new SomeDto(...))</c> rồi lọc <c>x.Id</c> ⇒ EF đẩy vị từ xuống TRƯỚC phép
/// chiếu và sinh ra <c>new SomeDto(...).Id == y</c> — Npgsql không dịch được.
/// <c>InMemory</c> KHÔNG bắt được lỗi này (không có bước dịch SQL) nên phải đo bằng Npgsql.
/// </para>
/// <para>
/// Gồm 2 mức:
/// <list type="number">
///   <item><b>Canary chạy trong CI</b> (<see cref="AssignmentLookups_Translate_OnNpgsql"/>): dùng
///   <c>ToQueryString()</c> — chỉ dịch, không kết nối (cùng kỹ thuật với
///   <c>AssetTimelineQueryTests.CursorTechnique_Translates_OnNpgsql</c>).</item>
///   <item><b>Hồi quy trên Postgres THẬT</b>
///   (<see cref="AssetDetail_AssignedTo_User_Department_SystemPosition_OnRealPostgres"/>): gọi thẳng handler
///   trên DB tạm; chỉ chạy khi có cờ <c>MIRATS_TEST_THROWAWAY_DB=1</c> (như test Concurrency) vì có ghi
///   fixture vào một database thật.</item>
/// </list>
/// </para>
/// </summary>
public class AssetDetailAssignmentTests
{
    /// <summary>Connection string tới database TẠM dùng cho hồi quy Postgres thật.</summary>
    public const string ConnectionStringEnv = "MIRATS_TEST_POSTGRES";

    private static GetAssetByIdQueryHandler Handler(AppDbContext db, ICompanyScopeService? scope = null)
        => new(db, scope ?? new TestHelpers.SuperUserScope());

    /// <summary>
    /// Canary CI — KHÔNG cần Postgres thật (<c>ToQueryString()</c> chỉ dịch, không kết nối).
    /// Ba hình dạng tra cứu <c>assignedTo</c> của handler phải dịch được sang SQL Npgsql. Hình dạng CŨ
    /// (<c>Select(DTO)</c> rồi lọc <c>.Id</c>) ném <c>InvalidOperationException ... could not be translated</c>.
    /// </summary>
    [Fact]
    public void AssignmentLookups_Translate_OnNpgsql()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=canary;Username=canary;Password=canary")
            .Options;
        using var db = new AppDbContext(options, new TestHelpers.SuperUserScope());

        var targetId = Guid.NewGuid();

        var userSql = db.Users.AsNoTracking()
            .Where(u => u.Id == targetId)
            .Select(u => new AssetAssignedToUserDto(u.Id, "user", u.Username, u.FirstName, u.LastName))
            .ToQueryString();
        Assert.Contains("WHERE", userSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Username", userSql);
        Assert.Contains("FirstName", userSql);

        var departmentSql = db.Departments.AsNoTracking()
            .Where(d => d.Id == targetId)
            .Select(d => new AssetAssignedToNamedDto(d.Id, "department", d.Name))
            .ToQueryString();
        Assert.Contains("WHERE", departmentSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Name", departmentSql);

        var positionSql = db.SystemPositions.AsNoTracking()
            .Where(sp => sp.Id == targetId)
            .Select(sp => new AssetAssignedToNamedDto(sp.Id, "systemPosition", sp.Name))
            .ToQueryString();
        Assert.Contains("WHERE", positionSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Name", positionSql);
    }

    /// <summary>
    /// Hồi quy Postgres THẬT (yêu cầu cờ <c>MIRATS_TEST_THROWAWAY_DB=1</c> + connection string DB tạm
    /// trong <see cref="ConnectionStringEnv"/>). Chạy thẳng
    /// <see cref="GetAssetByIdQueryHandler"/> — TRƯỚC khi fix, mọi bước <c>Handle</c> cho thiết bị đã gán
    /// đều ném <c>InvalidOperationException</c> (EF không dịch được) ⇒ test đổ.
    /// </summary>
    [ThrowawayDbFact]
    public async Task AssetDetail_AssignedTo_User_Department_SystemPosition_OnRealPostgres()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnv)?.Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                $"Thiếu {ConnectionStringEnv} (connection string tới DB TẠM). " +
                "VD: Host=127.0.0.1;Port=<host-port>;Database=bugq_verify;Username=postgres;Password=<pw>");

        // Migrations sống ở project aspire-react.Server (không phải assembly chứa DbContext) — phải chỉ
        // định tường minh y như PersistenceServiceCollectionExtensions, nếu không MigrateAsync() sẽ không
        // áp migration nào (bảng chưa tồn tại ⇒ test đổ vì lý do SAI).
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("aspire-react.Server"))
            .Options;
        await using var db = new AppDbContext(options, new TestHelpers.SuperUserScope());
        await db.Database.MigrateAsync();

        var tag = "BUGQ" + Guid.NewGuid().ToString("N")[..8];
        var codeNumber = Math.Abs(Guid.NewGuid().GetHashCode()) % 900 + 100;

        var assignee = new User
        {
            Username = tag.ToLowerInvariant(),
            FirstName = "Bug",
            LastName = "Q",
            Email = $"{tag}@test.local",
            IsActive = true,
        };
        var department = new Department { Name = $"{tag}-dept" };
        var systemInfo = new SystemInfo { Code = $"BUGQ-2026-{codeNumber:000}", Name = $"{tag}-system" };
        db.Users.Add(assignee);
        db.Departments.Add(department);
        db.SystemInfos.Add(systemInfo);
        await db.SaveChangesAsync();

        var position = new SystemPosition
        {
            SystemInfoId = systemInfo.Id,
            Code = $"{tag}-pos",
            Name = $"{tag}-position",
        };
        db.SystemPositions.Add(position);
        await db.SaveChangesAsync();

        var byUser = await SeedAssignedAssetAsync(db, $"{tag}-u", assignee.Id, assignee.Id, AssignmentTargetType.User);
        var byDepartment = await SeedAssignedAssetAsync(db, $"{tag}-d", department.Id, assignee.Id, AssignmentTargetType.Department);
        var byPosition = await SeedAssignedAssetAsync(db, $"{tag}-p", position.Id, assignee.Id, AssignmentTargetType.SystemPosition);

        var noAssignment = new Asset { AssetTag = $"{tag}-none", Name = "BUG-Q control (khong gan ai)" };
        db.Assets.Add(noAssignment);
        await db.SaveChangesAsync();

        try
        {
            var handler = Handler(db);

            var userResult = await handler.Handle(new GetAssetByIdQuery(byUser.Id), CancellationToken.None);
            Assert.True(userResult.Success, "Thiết bị gắn User phải load được (trước fix: 500 do EF không dịch được).");
            var userDto = Assert.IsType<AssetAssignedToUserDto>(userResult.Asset!.AssignedTo);
            Assert.Equal(assignee.Id, userDto.Id);
            Assert.Equal("user", userDto.Type);
            Assert.Equal(assignee.Username, userDto.Username);
            Assert.Equal("Bug", userDto.FirstName);

            var departmentResult = await handler.Handle(new GetAssetByIdQuery(byDepartment.Id), CancellationToken.None);
            Assert.True(departmentResult.Success, "Thiết bị gắn Department phải load được.");
            var departmentDto = Assert.IsType<AssetAssignedToNamedDto>(departmentResult.Asset!.AssignedTo);
            Assert.Equal(department.Id, departmentDto.Id);
            Assert.Equal("department", departmentDto.Type);
            Assert.Equal(department.Name, departmentDto.Name);

            var positionResult = await handler.Handle(new GetAssetByIdQuery(byPosition.Id), CancellationToken.None);
            Assert.True(positionResult.Success, "Thiết bị gắn SystemPosition phải load được.");
            var positionDto = Assert.IsType<AssetAssignedToNamedDto>(positionResult.Asset!.AssignedTo);
            Assert.Equal(position.Id, positionDto.Id);
            Assert.Equal("systemPosition", positionDto.Type);
            Assert.Equal(position.Name, positionDto.Name);

            var noneResult = await handler.Handle(new GetAssetByIdQuery(noAssignment.Id), CancellationToken.None);
            Assert.True(noneResult.Success);
            Assert.Null(noneResult.Asset!.AssignedTo);
        }
        finally
        {
            // DB tạm: dọn best-effort, KHÔNG để lỗi dọn dẹp che kết quả test.
            try
            {
                var assetIds = await db.Assets.Where(a => a.AssetTag.StartsWith(tag)).Select(a => a.Id).ToListAsync();
                db.Assignments.RemoveRange(await db.Assignments.Where(a => assetIds.Contains(a.AssetId)).ToListAsync());
                db.Assets.RemoveRange(await db.Assets.Where(a => assetIds.Contains(a.Id)).ToListAsync());
                db.SystemPositions.RemoveRange(await db.SystemPositions.Where(p => p.SystemInfoId == systemInfo.Id).ToListAsync());
                db.SystemInfos.RemoveRange(await db.SystemInfos.Where(s => s.Id == systemInfo.Id).ToListAsync());
                db.Departments.RemoveRange(await db.Departments.Where(d => d.Id == department.Id).ToListAsync());
                db.Users.RemoveRange(await db.Users.Where(u => u.Id == assignee.Id).ToListAsync());
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BUG-Q test] cleanup best-effort bỏ qua: {ex.Message}");
            }
        }
    }

    private static async Task<Asset> SeedAssignedAssetAsync(
        AppDbContext db, string assetTag, Guid targetId, Guid assignedById, AssignmentTargetType targetType)
    {
        var asset = new Asset { AssetTag = assetTag, Name = $"BUG-Q {assetTag}" };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();

        var assignment = new Assignment
        {
            AssetId = asset.Id,
            TargetType = targetType,
            TargetId = targetId,
            AssignedById = assignedById,
        };
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        asset.CurrentAssignmentId = assignment.Id;
        await db.SaveChangesAsync();
        return asset;
    }
}
