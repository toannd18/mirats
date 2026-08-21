using aspire_react.Server.Application.Assets.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using aspire_react.Server.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// ST9/F41 — Asset module coverage: CreateAssetCommand (handler + validator), DeleteAssetCommand
/// guards (confirmed / checked-out / history / component usage), company-scoped list & detail,
/// and the CheckoutAssetCommandValidator deployability rules.
///
/// NOTE: the CheckoutAssetCommandHandler / CheckinAssetCommandHandler bodies use
/// <c>FromSqlRaw("... FOR UPDATE")</c> + a real transaction, which the EF InMemory provider cannot
/// execute (empirically verified during ST9). Their business rules are therefore covered here at
/// the validator level (deployability, target existence, SystemPosition location requirement);
/// the transactional handler path is exercised on a relational provider in integration testing.
/// </summary>
public class AssetTests
{
    private static Guid ActorId { get; } = Guid.NewGuid();

    private static async Task<(Guid companyId, Guid categoryId, Guid modelId)> SeedMasterAsync(AppDbContext ctx)
    {
        var company = new Company { Name = "CT-A" };
        ctx.Companies.Add(company);
        var category = new Category { Name = "Phần cứng", CategoryType = CategoryType.Asset };
        ctx.Categories.Add(category);
        await ctx.SaveChangesAsync();

        var model = new AssetModel { Name = "Server Rack", CategoryId = category.Id };
        ctx.Models.Add(model);
        await ctx.SaveChangesAsync();
        return (company.Id, category.Id, model.Id);
    }

    private static async Task<Guid> SeedPendingAssetAsync(AppDbContext ctx, string tag, Guid? companyId, bool confirmed = true)
    {
        var asset = new Asset { AssetTag = tag, Name = tag, IsConfirmed = confirmed, CompanyId = companyId };
        ctx.Assets.Add(asset);
        await ctx.SaveChangesAsync();
        return asset.Id;
    }

    private static async Task<Guid> SeedActiveUserAsync(AppDbContext ctx, Guid companyId, string username = "u1")
    {
        var user = new User { Username = username, Email = $"{username}@test.local", FirstName = "A", LastName = "B", CompanyId = companyId, IsActive = true };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    // ==================== CREATE ====================

    [Fact]
    public async Task Create_Succeeds_CreatesAsset_AndLogsCreateWithCompanyId()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Create_Succeeds_CreatesAsset_AndLogsCreateWithCompanyId));
        var (companyId, _, modelId) = await SeedMasterAsync(ctx);
        var actionLog = TestHelpers.CreateActionLogService(ctx, ActorId);
        var handler = new CreateAssetCommandHandler(ctx, actionLog, new TestHelpers.SuperUserScope(), new AssetTagGenerator(ctx));

        var result = await handler.Handle(new CreateAssetCommand
        {
            AssetTag = "AST-001", Name = "Server 01", Serial = "SN-1",
            ModelId = modelId, CompanyId = companyId, CurrentUserId = ActorId
        }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.AssetId);
        var asset = await ctx.Assets.SingleAsync(a => a.AssetTag == "AST-001");
        Assert.True(asset.IsConfirmed); // the "Xác nhận tạo" button IS the final confirmation
        Assert.Equal(companyId, asset.CompanyId);

        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.Asset && l.ActionType == ActionType.Create);
        Assert.Equal(ActorId, log.CreatedBy);
        Assert.Equal(companyId, log.CompanyId);
        Assert.Contains("AST-001", log.Note);
    }

    [Fact]
    public async Task Create_DuplicateAssetTag_ValidatorRejects()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Create_DuplicateAssetTag_ValidatorRejects));
        await SeedPendingAssetAsync(ctx, "AST-001", companyId: null, confirmed: true);
        var validator = new CreateAssetCommandValidator(ctx);

        var validation = await validator.ValidateAsync(new CreateAssetCommand { AssetTag = "AST-001", Name = "Dup" });

        Assert.False(validation.IsValid);

        Assert.Contains(validation.Errors, e => e.PropertyName == nameof(CreateAssetCommand.AssetTag));
    }

    // ==================== UPDATE (Task F — patch semantics + confirmed field-lock) ====================
    // Rule đã xác nhận: asset ĐÃ confirmed → CHỈ Name/Notes sửa được; CHƯA confirmed → sửa mọi field.
    // Lỗi cũ: gate so field-absent (null/default) như "đã đổi" → chặn nhầm Name/Notes trên confirmed;
    // và apply vô điều kiện → xóa nhầm AssetTag/Serial khi payload một phần.

    [Fact]
    public async Task Update_Unconfirmed_AllowsAllFields_IncludingSerialAndCompany()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Update_Unconfirmed_AllowsAllFields_IncludingSerialAndCompany));
        var company = new Company { Name = "CT-A" };
        ctx.Companies.Add(company);
        await ctx.SaveChangesAsync();
        var asset = new Asset { AssetTag = "AST-001", Name = "Old", Serial = "SN-OLD", IsConfirmed = false, Physical = true, Requestable = false };
        ctx.Assets.Add(asset);
        await ctx.SaveChangesAsync();
        var actionLog = TestHelpers.CreateActionLogService(ctx, ActorId);
        var handler = new UpdateAssetCommandHandler(ctx, actionLog, new TestHelpers.SuperUserScope());

        var result = await handler.Handle(new UpdateAssetCommand
        {
            Id = asset.Id, AssetTag = "AST-001", Name = "New Name", Serial = "SN-NEW",
            CompanyId = company.Id, Notes = "edited", CurrentUserId = ActorId
        }, CancellationToken.None);

        Assert.True(result.Success);
        var updated = await ctx.Assets.SingleAsync(a => a.Id == asset.Id);
        Assert.Equal("New Name", updated.Name);
        Assert.Equal("SN-NEW", updated.Serial);
        Assert.Equal(company.Id, updated.CompanyId);
        Assert.Equal("edited", updated.Notes);
    }

    [Fact]
    public async Task Update_Confirmed_AllowsNameAndNotes_AndPreservesLockedFields()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Update_Confirmed_AllowsNameAndNotes_AndPreservesLockedFields));
        // Confirmed asset WITH a serial — partial payload (chỉ Name/Notes) không được bị chặn nhầm.
        var asset = new Asset { AssetTag = "AST-001", Name = "Old", Serial = "SN-KEEP", IsConfirmed = true };
        ctx.Assets.Add(asset);
        await ctx.SaveChangesAsync();
        var actionLog = TestHelpers.CreateActionLogService(ctx, ActorId);
        var handler = new UpdateAssetCommandHandler(ctx, actionLog, new TestHelpers.SuperUserScope());

        var result = await handler.Handle(new UpdateAssetCommand
        {
            Id = asset.Id, AssetTag = "", Name = "Renamed", Notes = "new notes", CurrentUserId = ActorId
        }, CancellationToken.None);

        Assert.True(result.Success);
        var updated = await ctx.Assets.SingleAsync(a => a.Id == asset.Id);
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal("new notes", updated.Notes);
        Assert.Equal("SN-KEEP", updated.Serial);    // field khóa được giữ nguyên
        Assert.Equal("AST-001", updated.AssetTag);  // tag KHÔNG bị xóa vì AssetTag rỗng
    }

    [Fact]
    public async Task Update_Confirmed_BlocksLockedFieldSerialChange()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Update_Confirmed_BlocksLockedFieldSerialChange));
        var asset = new Asset { AssetTag = "AST-001", Name = "Old", Serial = "SN-KEEP", IsConfirmed = true };
        ctx.Assets.Add(asset);
        await ctx.SaveChangesAsync();
        var actionLog = TestHelpers.CreateActionLogService(ctx, ActorId);
        var handler = new UpdateAssetCommandHandler(ctx, actionLog, new TestHelpers.SuperUserScope());

        var result = await handler.Handle(new UpdateAssetCommand
        {
            Id = asset.Id, AssetTag = "AST-001", Name = "Old", Serial = "SN-CHANGED", CurrentUserId = ActorId
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("CONFIRMED_ASSET_LOCKED", result.ErrorCode);
        Assert.Contains("Serial", result.Message);
        var updated = await ctx.Assets.SingleAsync(a => a.Id == asset.Id);
        Assert.Equal("SN-KEEP", updated.Serial); // không bị thay đổi
    }

    [Fact]
    public async Task Update_Unconfirmed_PartialPayload_PreservesAbsentFields()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Update_Unconfirmed_PartialPayload_PreservesAbsentFields));
        var asset = new Asset { AssetTag = "AST-001", Name = "Old", Serial = "SN-KEEP", OrderNumber = "ORD-1", IsConfirmed = false };
        ctx.Assets.Add(asset);
        await ctx.SaveChangesAsync();
        var actionLog = TestHelpers.CreateActionLogService(ctx, ActorId);
        var handler = new UpdateAssetCommandHandler(ctx, actionLog, new TestHelpers.SuperUserScope());

        var result = await handler.Handle(new UpdateAssetCommand
        {
            Id = asset.Id, AssetTag = "", Name = "Renamed", Notes = "notes", CurrentUserId = ActorId
        }, CancellationToken.None);

        Assert.True(result.Success);
        var updated = await ctx.Assets.SingleAsync(a => a.Id == asset.Id);
        Assert.Equal("AST-001", updated.AssetTag);   // KHÔNG bị xóa
        Assert.Equal("SN-KEEP", updated.Serial);     // KHÔNG bị xóa
        Assert.Equal("ORD-1", updated.OrderNumber);  // KHÔNG bị xóa
        Assert.Equal("Renamed", updated.Name);
    }


    // ==================== DELETE GUARDS ====================

    [Fact]
    public async Task Delete_Nonexistent_ReturnsNotFoundError()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Delete_Nonexistent_ReturnsNotFoundError));
        var actionLog = TestHelpers.CreateActionLogService(ctx, ActorId);
        var handler = new DeleteAssetCommandHandler(ctx, actionLog, new TestHelpers.SuperUserScope());

        var result = await handler.Handle(new DeleteAssetCommand { AssetId = Guid.NewGuid(), CurrentUserId = ActorId }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task Delete_Confirmed_Rejected()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Delete_Confirmed_Rejected));
        var assetId = await SeedPendingAssetAsync(ctx, "AST-001", companyId: null, confirmed: true);
        var actionLog = TestHelpers.CreateActionLogService(ctx, ActorId);
        var handler = new DeleteAssetCommandHandler(ctx, actionLog, new TestHelpers.SuperUserScope());

        var result = await handler.Handle(new DeleteAssetCommand { AssetId = assetId, CurrentUserId = ActorId }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ASSET_CONFIRMED_CANNOT_DELETE", result.ErrorCode);
    }

    [Fact]
    public async Task Delete_CheckedOut_Rejected()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Delete_CheckedOut_Rejected));
        var (companyId, _, _) = await SeedMasterAsync(ctx);
        var assetId = await SeedPendingAssetAsync(ctx, "AST-001", companyId, confirmed: false);
        var userId = await SeedActiveUserAsync(ctx, companyId);
        var assignment = new Assignment { AssetId = assetId, TargetType = AssignmentTargetType.User, TargetId = userId, AssignedById = ActorId };
        ctx.Assignments.Add(assignment);
        await ctx.SaveChangesAsync();
        var asset = await ctx.Assets.SingleAsync(a => a.Id == assetId);
        asset.CurrentAssignmentId = assignment.Id;
        asset.Status = AssetStatus.Deployed;
        await ctx.SaveChangesAsync();
        var actionLog = TestHelpers.CreateActionLogService(ctx, ActorId);
        var handler = new DeleteAssetCommandHandler(ctx, actionLog, new TestHelpers.SuperUserScope());

        var result = await handler.Handle(new DeleteAssetCommand { AssetId = assetId, CurrentUserId = ActorId }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ASSET_CHECKED_OUT", result.ErrorCode);
    }

    [Fact]
    public async Task Delete_WithAssignmentHistory_Rejected()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Delete_WithAssignmentHistory_Rejected));
        var (companyId, _, _) = await SeedMasterAsync(ctx);
        var assetId = await SeedPendingAssetAsync(ctx, "AST-001", companyId, confirmed: false);
        var userId = await SeedActiveUserAsync(ctx, companyId);
        ctx.Assignments.Add(new Assignment { AssetId = assetId, TargetType = AssignmentTargetType.User, TargetId = userId, AssignedById = ActorId });
        await ctx.SaveChangesAsync();
        var actionLog = TestHelpers.CreateActionLogService(ctx, ActorId);
        var handler = new DeleteAssetCommandHandler(ctx, actionLog, new TestHelpers.SuperUserScope());

        var result = await handler.Handle(new DeleteAssetCommand { AssetId = assetId, CurrentUserId = ActorId }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ASSET_HAS_ASSIGNMENTS", result.ErrorCode);
    }

    [Fact]
    public async Task Delete_WithMaintenanceHistory_Rejected()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Delete_WithMaintenanceHistory_Rejected));
        var (companyId, _, _) = await SeedMasterAsync(ctx);
        var assetId = await SeedPendingAssetAsync(ctx, "AST-001", companyId, confirmed: false);
        ctx.AssetMaintenances.Add(new AssetMaintenance
        {
            AssetId = assetId, Title = "Bảo trì định kỳ", CompanyId = companyId,
            StartDate = DateTime.UtcNow.AddDays(-1)
        });
        await ctx.SaveChangesAsync();
        var actionLog = TestHelpers.CreateActionLogService(ctx, ActorId);
        var handler = new DeleteAssetCommandHandler(ctx, actionLog, new TestHelpers.SuperUserScope());

        var result = await handler.Handle(new DeleteAssetCommand { AssetId = assetId, CurrentUserId = ActorId }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ASSET_HAS_MAINTENANCES", result.ErrorCode);
    }

    [Fact]
    public async Task Delete_UsedByComponent_Rejected()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Delete_UsedByComponent_Rejected));
        var (companyId, _, _) = await SeedMasterAsync(ctx);
        var assetId = await SeedPendingAssetAsync(ctx, "AST-001", companyId, confirmed: false);
        ctx.ComponentAssignments.Add(new ComponentAssignment { AssetId = assetId, ComponentId = Guid.NewGuid(), AssignedQty = 1 });
        await ctx.SaveChangesAsync();
        var actionLog = TestHelpers.CreateActionLogService(ctx, ActorId);
        var handler = new DeleteAssetCommandHandler(ctx, actionLog, new TestHelpers.SuperUserScope());

        var result = await handler.Handle(new DeleteAssetCommand { AssetId = assetId, CurrentUserId = ActorId }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ASSET_USED_BY_COMPONENT", result.ErrorCode);
    }

    [Fact]
    public async Task Delete_UnconfirmedNoHistory_Succeeds_AndLogsDelete()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Delete_UnconfirmedNoHistory_Succeeds_AndLogsDelete));
        var (companyId, _, _) = await SeedMasterAsync(ctx);
        var assetId = await SeedPendingAssetAsync(ctx, "AST-001", companyId, confirmed: false);
        var actionLog = TestHelpers.CreateActionLogService(ctx, ActorId);
        var handler = new DeleteAssetCommandHandler(ctx, actionLog, new TestHelpers.SuperUserScope());

        var result = await handler.Handle(new DeleteAssetCommand { AssetId = assetId, CurrentUserId = ActorId }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Empty(await ctx.Assets.Where(a => a.Id == assetId).ToListAsync());
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.Asset && l.ActionType == ActionType.Delete);
        Assert.Equal(companyId, log.CompanyId);
        Assert.Equal(ActorId, log.CreatedBy);
    }


    // ==================== COMPANY SCOPE (controller-level list/detail) ====================

    [Fact]
    public async Task GetAssets_RegularUser_SeesOnlyOwnCompanyAndFloaters()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(GetAssets_RegularUser_SeesOnlyOwnCompanyAndFloaters));
        var companyA = new Company { Name = "CT-A" };
        var companyB = new Company { Name = "CT-B" };
        ctx.Companies.AddRange(companyA, companyB);
        ctx.Assets.AddRange(
            new Asset { AssetTag = "AST-A", Name = "A", IsConfirmed = true, CompanyId = companyA.Id },
            new Asset { AssetTag = "AST-B", Name = "B", IsConfirmed = true, CompanyId = companyB.Id },
            new Asset { AssetTag = "AST-F", Name = "F", IsConfirmed = true, CompanyId = null });
        await ctx.SaveChangesAsync();

        var controller = new AssetsController(ctx, new TestHelpers.ThrowingMediator(), new TestHelpers.FakeCurrentUser(),
            new TestHelpers.FakeScope { Super = false, CompanyId = companyA.Id });

        var result = await controller.GetAssets(null, null, null, null);

        var ok = Assert.IsType<OkObjectResult>(result);
        var tags = TestHelpers.ReadDataStringArray(ok.Value, "assetTag");
        Assert.Contains("AST-A", tags);
        Assert.Contains("AST-F", tags);      // company-less assets remain visible to everyone
        Assert.DoesNotContain("AST-B", tags); // company B asset must NOT leak
    }

    [Fact]
    public async Task GetAssets_SuperUser_SeesAllCompanies()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(GetAssets_SuperUser_SeesAllCompanies));
        var companyA = new Company { Name = "CT-A" };
        var companyB = new Company { Name = "CT-B" };
        ctx.Companies.AddRange(companyA, companyB);
        ctx.Assets.AddRange(
            new Asset { AssetTag = "AST-A", Name = "A", IsConfirmed = true, CompanyId = companyA.Id },
            new Asset { AssetTag = "AST-B", Name = "B", IsConfirmed = true, CompanyId = companyB.Id });
        await ctx.SaveChangesAsync();

        var controller = new AssetsController(ctx, new TestHelpers.ThrowingMediator(), new TestHelpers.FakeCurrentUser(),
            new TestHelpers.FakeScope { Super = true });

        var result = await controller.GetAssets(null, null, null, null);

        var ok = Assert.IsType<OkObjectResult>(result);
        var tags = TestHelpers.ReadDataStringArray(ok.Value, "assetTag");
        Assert.Contains("AST-A", tags);
        Assert.Contains("AST-B", tags);
    }

    [Fact]
    public async Task GetAsset_RegularUser_OtherCompany_ReturnsNotFound()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(GetAsset_RegularUser_OtherCompany_ReturnsNotFound));
        var companyA = new Company { Name = "CT-A" };
        var companyB = new Company { Name = "CT-B" };
        ctx.Companies.AddRange(companyA, companyB);
        var assetB = new Asset { AssetTag = "AST-B", Name = "B", IsConfirmed = true, CompanyId = companyB.Id };
        ctx.Assets.Add(assetB);
        await ctx.SaveChangesAsync();

        var controller = new AssetsController(ctx, new TestHelpers.ThrowingMediator(), new TestHelpers.FakeCurrentUser(),
            new TestHelpers.FakeScope { Super = false, CompanyId = companyA.Id });

        var result = await controller.GetAsset(assetB.Id);

        Assert.IsType<NotFoundObjectResult>(result);
    }


    // ==================== CHECKOUT VALIDATOR (deployability rules) ====================
    // The handler itself uses FromSqlRaw FOR UPDATE + transaction (not InMemory-compatible),
    // so the checkout/checkin business rules are exercised through the FluentValidation layer.

    private static async Task<(Guid assetId, Guid userId)> SeedCheckoutContextAsync(AppDbContext ctx, AssetStatus status, Guid? currentAssignmentId = null)
    {
        var (companyId, _, _) = await SeedMasterAsync(ctx);
        var asset = new Asset { AssetTag = "AST-C", Name = "CheckoutMe", IsConfirmed = true, Status = status, CompanyId = companyId, CurrentAssignmentId = currentAssignmentId };
        ctx.Assets.Add(asset);
        var user = new User { Username = "target", Email = "target@test.local", FirstName = "A", LastName = "B", CompanyId = companyId, IsActive = true };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return (asset.Id, user.Id);
    }

    [Fact]
    public async Task Checkout_Validator_PendingNotCheckedOut_ToActiveUser_IsValid()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Checkout_Validator_PendingNotCheckedOut_ToActiveUser_IsValid));
        var (assetId, userId) = await SeedCheckoutContextAsync(ctx, AssetStatus.Pending);
        var validator = new CheckoutAssetCommandValidator(ctx);

        var validation = await validator.ValidateAsync(new CheckoutAssetCommand(assetId, AssignmentTargetType.User, userId, null, null, null, ActorId));

        Assert.True(validation.IsValid);
    }

    [Fact]
    public async Task Checkout_Validator_ArchivedOrMissingAsset_Fails()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Checkout_Validator_ArchivedOrMissingAsset_Fails));
        var (assetId, userId) = await SeedCheckoutContextAsync(ctx, AssetStatus.Archived);
        var validator = new CheckoutAssetCommandValidator(ctx);

        var archived = await validator.ValidateAsync(new CheckoutAssetCommand(assetId, AssignmentTargetType.User, userId, null, null, null, ActorId));
        Assert.False(archived.IsValid);

        var missing = await validator.ValidateAsync(new CheckoutAssetCommand(Guid.NewGuid(), AssignmentTargetType.User, userId, null, null, null, ActorId));
        Assert.False(missing.IsValid);
    }

    [Fact]
    public async Task Checkout_Validator_NotPending_Fails()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Checkout_Validator_NotPending_Fails));
        var (assetId, userId) = await SeedCheckoutContextAsync(ctx, AssetStatus.Deployed);
        var validator = new CheckoutAssetCommandValidator(ctx);

        var validation = await validator.ValidateAsync(new CheckoutAssetCommand(assetId, AssignmentTargetType.User, userId, null, null, null, ActorId));

        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task Checkout_Validator_AlreadyCheckedOut_Fails()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Checkout_Validator_AlreadyCheckedOut_Fails));
        var (assetId, userId) = await SeedCheckoutContextAsync(ctx, AssetStatus.Pending, currentAssignmentId: Guid.NewGuid());
        var validator = new CheckoutAssetCommandValidator(ctx);

        var validation = await validator.ValidateAsync(new CheckoutAssetCommand(assetId, AssignmentTargetType.User, userId, null, null, null, ActorId));

        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task Checkout_Validator_SystemPositionRequiresLocation_Fails()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Checkout_Validator_SystemPositionRequiresLocation_Fails));
        var (companyId, _, _) = await SeedMasterAsync(ctx);
        var sysInfo = new SystemInfo { Name = "Hệ thống A", CompanyId = companyId };
        var position = new SystemPosition { Name = "Vị trí 1", SystemInfo = sysInfo };
        ctx.SystemInfos.Add(sysInfo);
        ctx.SystemPositions.Add(position);
        await ctx.SaveChangesAsync();
        var asset = new Asset { AssetTag = "AST-S", Name = "S", IsConfirmed = true, Status = AssetStatus.Pending, CompanyId = companyId };
        ctx.Assets.Add(asset);
        await ctx.SaveChangesAsync();
        var validator = new CheckoutAssetCommandValidator(ctx);

        // TargetType=SystemPosition requires LocationId — omitted here.
        var validation = await validator.ValidateAsync(new CheckoutAssetCommand(asset.Id, AssignmentTargetType.SystemPosition, position.Id, null, null, null, ActorId));

        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task Checkout_Validator_UnknownTarget_Fails()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Checkout_Validator_UnknownTarget_Fails));
        var (assetId, _) = await SeedCheckoutContextAsync(ctx, AssetStatus.Pending);
        var validator = new CheckoutAssetCommandValidator(ctx);

        var validation = await validator.ValidateAsync(new CheckoutAssetCommand(assetId, AssignmentTargetType.User, Guid.NewGuid(), null, null, null, ActorId));

        Assert.False(validation.IsValid);
    }
}

