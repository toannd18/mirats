using aspire_react.Server.Application.Assets.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [FIX-GUID-EMPTY 2026-10-20] Nhóm lỗi: trường <c>Guid</c> KHÓA NGOẠI bắt buộc nhận <c>Guid.Empty</c>
/// (body thiếu field ⇒ DTO dùng <c>Guid</c> không nullable ⇒ bind 0) rồi handler ghi thẳng vào cột FK
/// ⇒ Postgres 23503 ⇒ HTTP <b>500</b>. Đây là lỗi quan sát thật: <c>POST /assets/{id}/archive</c> với
/// body <c>{}</c>.
/// <para>
/// Phủ 3 nhóm đã sửa: <b>(A) archive</b>, <b>(B) checkin</b>, <b>(C) checkout tới SystemPosition</b>.
/// Tầng validator kiểm trên InMemory (chỉ truy vấn <c>Locations</c>); tầng handler kiểm trên **Postgres
/// THẬT** vì InMemory KHÔNG thực thi FK — nếu chỉ test InMemory thì bug 500 vẫn "xanh" giả.
/// </para>
/// </summary>
public class GuidFkValidationTests
{
    /// <summary>Biến môi trường trỏ tới DB TẠM (container Postgres riêng) — bắt buộc với test Postgres.</summary>
    public const string ConnectionStringEnv = "MIRATS_TEST_POSTGRES";

    private static readonly Guid ActorId = Guid.NewGuid();

    // ==================== A. ARCHIVE ====================

    [Fact]
    public void Archive_Validator_EmptyLocation_Rejects_WithLocationRequired()
    {
        var validation = new ArchiveAssetCommandValidator()
            .Validate(new ArchiveAssetCommand(Guid.NewGuid(), Guid.Empty, ActorId));

        Assert.False(validation.IsValid);
        var failure = Assert.Single(validation.Errors, e => e.PropertyName == nameof(ArchiveAssetCommand.LocationId));
        Assert.Equal("LOCATION_REQUIRED", failure.ErrorCode);
    }

    [Fact]
    public void Archive_Validator_RealLocation_Passes()
    {
        var validation = new ArchiveAssetCommandValidator()
            .Validate(new ArchiveAssetCommand(Guid.NewGuid(), Guid.NewGuid(), ActorId));

        Assert.True(validation.IsValid);
    }

    [ThrowawayDbFact]
    public async Task Archive_Handler_EmptyLocation_OnRealPostgres_ReturnsResourceNotFound_NotFk500()
    {
        await using var db = await CreatePostgresContextAsync();
        var actor = await SeedActorAsync(db);
        var asset = await SeedPendingAssetAsync(db);

        var handler = new ArchiveAssetCommandHandler(db, TestHelpers.CreateActionLogService(db, actor.Id));

        // TRƯỚC khi fix: Guid.Empty ghi thẳng vào assets.LocationId ⇒ FK 23503 ⇒ exception (500).
        // NAY: chặn trước khi chạm FK ⇒ trả result 400 RESOURCE_NOT_FOUND, không ném.
        var result = await handler.Handle(new ArchiveAssetCommand(asset.Id, Guid.Empty, actor.Id), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("RESOURCE_NOT_FOUND", result.ErrorCode);
        Assert.Equal(AssetStatus.Pending, await db.Assets.AsNoTracking()
            .Where(a => a.Id == asset.Id).Select(a => a.Status).SingleAsync());
    }

    [ThrowawayDbFact]
    public async Task Archive_Handler_UnknownLocation_OnRealPostgres_ReturnsResourceNotFound()
    {
        await using var db = await CreatePostgresContextAsync();
        var actor = await SeedActorAsync(db);
        var asset = await SeedPendingAssetAsync(db);
        var handler = new ArchiveAssetCommandHandler(db, TestHelpers.CreateActionLogService(db, actor.Id));

        var result = await handler.Handle(new ArchiveAssetCommand(asset.Id, Guid.NewGuid(), actor.Id), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("RESOURCE_NOT_FOUND", result.ErrorCode);
    }

    [ThrowawayDbFact]
    public async Task Archive_Handler_ValidLocation_OnRealPostgres_Archives()
    {
        await using var db = await CreatePostgresContextAsync();
        var actor = await SeedActorAsync(db);
        var asset = await SeedPendingAssetAsync(db);
        var location = await SeedLocationAsync(db);
        var handler = new ArchiveAssetCommandHandler(db, TestHelpers.CreateActionLogService(db, actor.Id));

        var result = await handler.Handle(new ArchiveAssetCommand(asset.Id, location.Id, actor.Id), CancellationToken.None);

        Assert.True(result.Success, result.Message);
        var reloaded = await db.Assets.AsNoTracking().SingleAsync(a => a.Id == asset.Id);
        Assert.Equal(AssetStatus.Archived, reloaded.Status);
        Assert.Equal(location.Id, reloaded.LocationId);
    }

    [Fact]
    public async Task Archive_Mediator_EmptyLocation_IsBlockedByValidationPipeline()
    {
        await using var db = TestHelpers.CreateContext(nameof(Archive_Mediator_EmptyLocation_IsBlockedByValidationPipeline));
        var asset = new Asset { AssetTag = "GUID-PIPE-1", Name = "pipe", Status = AssetStatus.Pending };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();

        var mediator = TestHelpers.BuildMediator(db, new TestHelpers.SuperUserScope(), ActorId);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            mediator.Send(new ArchiveAssetCommand(asset.Id, Guid.Empty, ActorId)));

        Assert.Contains(ex.Errors, e => e.ErrorCode == "LOCATION_REQUIRED");
    }

    // ==================== B. CHECKIN ====================

    [Fact]
    public void Checkin_Validator_EmptyLocation_Rejects_WithLocationRequired()
    {
        var validation = new CheckinAssetCommandValidator()
            .Validate(new CheckinAssetCommand(Guid.NewGuid(), Guid.Empty, null, null, ActorId));

        Assert.False(validation.IsValid);
        var failure = Assert.Single(validation.Errors, e => e.PropertyName == nameof(CheckinAssetCommand.LocationId));
        Assert.Equal("LOCATION_REQUIRED", failure.ErrorCode);
    }

    [Fact]
    public void Checkin_Validator_RealLocation_Passes()
    {
        var validation = new CheckinAssetCommandValidator()
            .Validate(new CheckinAssetCommand(Guid.NewGuid(), Guid.NewGuid(), null, null, ActorId));

        Assert.True(validation.IsValid);
    }

    [ThrowawayDbFact]
    public async Task Checkin_Handler_EmptyLocation_OnRealPostgres_ReturnsResourceNotFound_NotFk500()
    {
        await using var db = await CreatePostgresContextAsync();
        var actor = await SeedActorAsync(db);
        var asset = await SeedDeployedAssetAsync(db, actor.Id);
        var handler = new CheckinAssetCommandHandler(db, TestHelpers.CreateActionLogService(db, actor.Id));

        var result = await handler.Handle(
            new CheckinAssetCommand(asset.Id, Guid.Empty, null, null, actor.Id), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("RESOURCE_NOT_FOUND", result.ErrorCode);
        Assert.Equal(AssetStatus.Deployed, await db.Assets.AsNoTracking()
            .Where(a => a.Id == asset.Id).Select(a => a.Status).SingleAsync());
    }

    [ThrowawayDbFact]
    public async Task Checkin_Handler_UnknownLocation_OnRealPostgres_ReturnsResourceNotFound()
    {
        await using var db = await CreatePostgresContextAsync();
        var actor = await SeedActorAsync(db);
        var asset = await SeedDeployedAssetAsync(db, actor.Id);
        var handler = new CheckinAssetCommandHandler(db, TestHelpers.CreateActionLogService(db, actor.Id));

        var result = await handler.Handle(
            new CheckinAssetCommand(asset.Id, Guid.NewGuid(), null, null, actor.Id), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("RESOURCE_NOT_FOUND", result.ErrorCode);
    }

    // ==================== C. CHECKOUT → SYSTEM POSITION ====================

    [Fact]
    public async Task Checkout_Validator_SystemPosition_EmptyLocation_Rejects_WithLocationRequired()
    {
        await using var db = TestHelpers.CreateContext(nameof(Checkout_Validator_SystemPosition_EmptyLocation_Rejects_WithLocationRequired));
        var (assetId, positionId, _) = await SeedCheckoutAsync(db);

        var validation = await new CheckoutAssetCommandValidator(db).ValidateAsync(
            new CheckoutAssetCommand(assetId, AssignmentTargetType.SystemPosition, positionId, null, null, Guid.Empty, ActorId));

        Assert.False(validation.IsValid);
        var failure = Assert.Single(validation.Errors, e => e.PropertyName == nameof(CheckoutAssetCommand.LocationId));
        Assert.Equal("LOCATION_REQUIRED", failure.ErrorCode);
    }

    [Fact]
    public async Task Checkout_Validator_SystemPosition_UnknownLocation_Rejects_WithResourceNotFound()
    {
        await using var db = TestHelpers.CreateContext(nameof(Checkout_Validator_SystemPosition_UnknownLocation_Rejects_WithResourceNotFound));
        var (assetId, positionId, _) = await SeedCheckoutAsync(db);

        var validation = await new CheckoutAssetCommandValidator(db).ValidateAsync(
            new CheckoutAssetCommand(assetId, AssignmentTargetType.SystemPosition, positionId, null, null, Guid.NewGuid(), ActorId));

        Assert.False(validation.IsValid);
        var failure = Assert.Single(validation.Errors, e => e.PropertyName == nameof(CheckoutAssetCommand.LocationId));
        Assert.Equal("RESOURCE_NOT_FOUND", failure.ErrorCode);
    }

    [Fact]
    public async Task Checkout_Validator_SystemPosition_KnownLocation_Passes()
    {
        await using var db = TestHelpers.CreateContext(nameof(Checkout_Validator_SystemPosition_KnownLocation_Passes));
        var (assetId, positionId, locationId) = await SeedCheckoutAsync(db);

        var validation = await new CheckoutAssetCommandValidator(db).ValidateAsync(
            new CheckoutAssetCommand(assetId, AssignmentTargetType.SystemPosition, positionId, null, null, locationId, ActorId));

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => $"{e.PropertyName}:{e.ErrorMessage}")));
    }

    // ==================== Accessory (bổ sung sau đối chiếu FE ↔ backend) ====================

    [Fact]
    public async Task UpdateAccessory_GuidEmpty_Clears_Supplier_Link()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(UpdateAccessory_GuidEmpty_Clears_Supplier_Link));
        var supplier = new Supplier { Code = "ACC-CLR", Name = "NCC phụ kiện" };
        ctx.Suppliers.Add(supplier);
        await ctx.SaveChangesAsync();
        var accessory = new Accessory { Name = "Chuột", Qty = 5, MinAmt = 1, SupplierId = supplier.Id };
        ctx.Accessories.Add(accessory);
        await ctx.SaveChangesAsync();

        var handler = new aspire_react.Server.Application.Accessories.Commands.UpdateAccessoryCommandHandler(
            ctx, new TestHelpers.SuperUserScope());

        var result = await handler.Handle(new aspire_react.Server.Application.Accessories.Commands.UpdateAccessoryCommand
        {
            Id = accessory.Id,
            SupplierId = Guid.Empty,
            CurrentUserId = ActorId
        }, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        var reloaded = await ctx.Accessories.AsNoTracking().SingleAsync(a => a.Id == accessory.Id);
        Assert.Null(reloaded.SupplierId);
    }

    [Fact]
    public async Task UpdateAccessory_Unknown_Supplier_Returns_400_ResourceNotFound()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(UpdateAccessory_Unknown_Supplier_Returns_400_ResourceNotFound));
        var accessory = new Accessory { Name = "Bàn phím", Qty = 2, MinAmt = 1 };
        ctx.Accessories.Add(accessory);
        await ctx.SaveChangesAsync();

        var handler = new aspire_react.Server.Application.Accessories.Commands.UpdateAccessoryCommandHandler(
            ctx, new TestHelpers.SuperUserScope());

        var result = await handler.Handle(new aspire_react.Server.Application.Accessories.Commands.UpdateAccessoryCommand
        {
            Id = accessory.Id,
            SupplierId = Guid.NewGuid(),
            CurrentUserId = ActorId
        }, CancellationToken.None);

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

    private static string UniqueTag(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    /// <summary>Actor PHẢI tồn tại trong DB thật: ActionLog.CreatedBy / Assignment.AssignedById là FK tới users.</summary>
    private static async Task<User> SeedActorAsync(AppDbContext db)
    {
        var user = new User
        {
            Username = UniqueTag("guid-actor").ToLowerInvariant(),
            Email = UniqueTag("guid-actor") + "@test.local",
            FirstName = "GUID",
            LastName = "Actor",
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<Location> SeedLocationAsync(AppDbContext db)
    {
        var location = new Location { Name = UniqueTag("GUID-LOC") };
        db.Locations.Add(location);
        await db.SaveChangesAsync();
        return location;
    }

    private static async Task<Asset> SeedPendingAssetAsync(AppDbContext db)
    {
        var asset = new Asset
        {
            AssetTag = UniqueTag("GUID-AST"),
            Name = "guid asset",
            Status = AssetStatus.Pending,
            IsConfirmed = true
        };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();
        return asset;
    }

    private static async Task<Asset> SeedDeployedAssetAsync(AppDbContext db, Guid actorId)
    {
        var user = new User
        {
            Username = UniqueTag("guid-user").ToLowerInvariant(),
            Email = UniqueTag("guid") + "@test.local",
            FirstName = "GUID",
            LastName = "User",
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var asset = new Asset
        {
            AssetTag = UniqueTag("GUID-DEP"),
            Name = "guid deployed",
            Status = AssetStatus.Deployed,
            IsConfirmed = true
        };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();

        var assignment = new Assignment
        {
            AssetId = asset.Id,
            TargetType = AssignmentTargetType.User,
            TargetId = user.Id,
            AssignedById = actorId
        };
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        asset.CurrentAssignmentId = assignment.Id;
        db.Assets.Update(asset);
        await db.SaveChangesAsync();
        return asset;
    }

    private static async Task<(Guid assetId, Guid positionId, Guid locationId)> SeedCheckoutAsync(AppDbContext db)
    {
        var code = UniqueTag("GUID-0001-001");
        var system = new SystemInfo { Code = code, Name = "guid system" };
        db.SystemInfos.Add(system);
        await db.SaveChangesAsync();

        var position = new SystemPosition { SystemInfoId = system.Id, Code = code, Name = "guid position" };
        db.SystemPositions.Add(position);

        var location = new Location { Name = UniqueTag("GUID-LOC") };
        db.Locations.Add(location);

        var asset = new Asset
        {
            AssetTag = UniqueTag("GUID-CHK"),
            Name = "guid checkout",
            Status = AssetStatus.Pending,
            IsConfirmed = true
        };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();

        return (asset.Id, position.Id, location.Id);
    }
}
