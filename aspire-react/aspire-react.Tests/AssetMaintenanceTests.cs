using aspire_react.Server.Application.AssetMaintenances.Queries;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using aspire_react.Server.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// Asset Maintenance: snapshot (BOTH SystemInfo + SystemPosition levels, location, user, department),
/// snapshot immutability, update whitelist/FIELD_LOCKED, validations, the superuser-only DELETE guard,
/// and company-scoped visibility (regular users see only their company; Superuser sees all).
/// </summary>
public class AssetMaintenanceTests
{
    private sealed class SuperUserScope : ICompanyScopeService
    {
        public bool IsSuperUser() => true;
        public Task<List<Guid>> GetUserCompanyIdsAsync() => Task.FromResult(new List<Guid>());
        public Task<Guid?> GetCurrentUserCompanyIdAsync() => Task.FromResult<Guid?>(null);
        public Task<bool> IsCompanyIdInUserScopeAsync(Guid companyId) => Task.FromResult(true);
    }

    private sealed class FakeCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid GetLocalUserId() => UserId;
    }

    private sealed class FakeScope : ICompanyScopeService
    {
        public bool Super { get; set; }
        public Guid? CompanyId { get; set; }
        public bool IsSuperUser() => Super;
        public Task<List<Guid>> GetUserCompanyIdsAsync() => Task.FromResult(new List<Guid>());
        public Task<Guid?> GetCurrentUserCompanyIdAsync() => Task.FromResult(Super ? (Guid?)null : CompanyId);
        public Task<bool> IsCompanyIdInUserScopeAsync(Guid companyId)
            => Task.FromResult(Super || CompanyId == null || CompanyId == companyId);
    }

    private static AppDbContext CreateContext(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options, new SuperUserScope());
    }

    private static async Task<(Guid assetId, SystemInfo sysInfo, SystemPosition pos, Location loc, Department dept, User user)>
        SeedAssetAsync(AppDbContext ctx)
    {
        var sysInfo = new SystemInfo { Name = "Hệ thống A" };
        var pos = new SystemPosition { Name = "Vị trí 1", SystemInfo = sysInfo };
        var loc = new Location { Name = "Kho A" };
        var dept = new Department { Name = "Phòng IT" };
        var user = new User { Username = "user1", FirstName = "Nguyen", LastName = "A", Department = dept };
        ctx.SystemInfos.Add(sysInfo);
        ctx.SystemPositions.Add(pos);
        ctx.Locations.Add(loc);
        ctx.Departments.Add(dept);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var asset = new Asset
        {
            AssetTag = "AST-001",
            Name = "Server 01",
            IsConfirmed = true,
            SystemPositionId = pos.Id,
            LocationId = loc.Id
        };
        ctx.Assets.Add(asset);
        await ctx.SaveChangesAsync();

        var asgn = new Assignment
        {
            AssetId = asset.Id,
            TargetType = AssignmentTargetType.User,
            TargetId = user.Id,
            AssignedById = Guid.NewGuid()
        };
        ctx.Assignments.Add(asgn);
        asset.CurrentAssignmentId = asgn.Id;
        await ctx.SaveChangesAsync();

        return (asset.Id, sysInfo, pos, loc, dept, user);
    }


    // ==================== Snapshot ====================

    [Fact]
    public async Task CreateMaintenance_SnapshotsBothSystemLevels_AndContext()
    {
        await using var ctx = CreateContext(nameof(CreateMaintenance_SnapshotsBothSystemLevels_AndContext));
        var (assetId, sysInfo, pos, loc, dept, user) = await SeedAssetAsync(ctx);
        var controller = new AssetMaintenancesController(TestHelpers.BuildMediator(ctx, new FakeScope { Super = true }), ctx, new FakeCurrentUser(), new FakeScope { Super = true }, TestHelpers.CreateActionLogService(ctx));

        var result = await controller.Create(assetId, new CreateAssetMaintenanceRequest(
            AssetMaintenanceType.Maintenance, "Bảo trì định kỳ", null, null,
            StartDate: DateTime.UtcNow, CompletionDate: null, Cost: 150.5m, IsWarranty: false));

        Assert.IsType<OkObjectResult>(result);
        var m = await ctx.AssetMaintenances.SingleAsync(x => x.AssetId == assetId);

        // BOTH levels must be captured separately (SystemInfo parent + SystemPosition child).
        Assert.Equal(sysInfo.Id, m.SnapshotSystemInfoId);
        Assert.Equal("Hệ thống A", m.SnapshotSystemInfoName);
        Assert.Equal(pos.Id, m.SnapshotSystemPositionId);
        Assert.Equal("Vị trí 1", m.SnapshotSystemPositionName);
        Assert.Equal(loc.Id, m.SnapshotLocationId);
        Assert.Equal("Kho A", m.SnapshotLocationName);
        Assert.Equal(user.Id, m.SnapshotAssignedUserId);
        Assert.Equal("Nguyen A", m.SnapshotAssignedUserName);
        Assert.Equal(dept.Id, m.SnapshotDepartmentId);
        Assert.Equal("Phòng IT", m.SnapshotDepartmentName);
        Assert.Equal(1, await ctx.ActionLogs.CountAsync(l => l.ItemType == ItemType.AssetMaintenance && l.ActionType == ActionType.Create));
    }

    [Fact]
    public async Task Snapshot_IsFrozen_AfterAssetMoves()
    {
        await using var ctx = CreateContext(nameof(Snapshot_IsFrozen_AfterAssetMoves));
        var (assetId, sysInfo, pos, loc, dept, user) = await SeedAssetAsync(ctx);
        var controller = new AssetMaintenancesController(TestHelpers.BuildMediator(ctx, new FakeScope { Super = true }), ctx, new FakeCurrentUser(), new FakeScope { Super = true }, TestHelpers.CreateActionLogService(ctx));
        await controller.Create(assetId, new CreateAssetMaintenanceRequest(
            AssetMaintenanceType.Repair, "Sửa nguồn", null, null, DateTime.UtcNow, DateTime.UtcNow.AddDays(1), 50m, true));

        var otherSys = new SystemInfo { Name = "Hệ thống B" };
        var otherPos = new SystemPosition { Name = "Vị trí 9", SystemInfo = otherSys };
        ctx.SystemInfos.Add(otherSys);
        ctx.SystemPositions.Add(otherPos);
        var asset = await ctx.Assets.SingleAsync(a => a.Id == assetId);
        asset.SystemPositionId = otherPos.Id;
        asset.LocationId = null;
        asset.CurrentAssignmentId = null;
        await ctx.SaveChangesAsync();

        var m = await ctx.AssetMaintenances.SingleAsync(x => x.AssetId == assetId);
        Assert.Equal(sysInfo.Id, m.SnapshotSystemInfoId);
        Assert.Equal(pos.Id, m.SnapshotSystemPositionId);
        Assert.Equal(loc.Id, m.SnapshotLocationId);
        Assert.Equal(user.Id, m.SnapshotAssignedUserId);
        Assert.Equal(dept.Id, m.SnapshotDepartmentId);
    }

    [Fact]
    public async Task CreateMaintenance_IncidentReport_WorksLikeOthers()
    {
        await using var ctx = CreateContext(nameof(CreateMaintenance_IncidentReport_WorksLikeOthers));
        var (assetId, _, _, _, _, _) = await SeedAssetAsync(ctx);
        var controller = new AssetMaintenancesController(TestHelpers.BuildMediator(ctx, new FakeScope { Super = true }), ctx, new FakeCurrentUser(), new FakeScope { Super = true }, TestHelpers.CreateActionLogService(ctx));

        var result = await controller.Create(assetId, new CreateAssetMaintenanceRequest(
            AssetMaintenanceType.IncidentReport, "Phát hiện sự cố", "chờ xử lý", null,
            StartDate: DateTime.UtcNow, CompletionDate: null, Cost: null, IsWarranty: false));

        Assert.IsType<OkObjectResult>(result);
        var m = await ctx.AssetMaintenances.SingleAsync(x => x.AssetId == assetId);
        Assert.Equal(AssetMaintenanceType.IncidentReport, m.Type);
        Assert.Null(m.CompletionDate);
    }


    // ==================== Validations / update whitelist ====================

    [Fact]
    public async Task CreateMaintenance_CompletionBeforeStart_Rejected()
    {
        await using var ctx = CreateContext(nameof(CreateMaintenance_CompletionBeforeStart_Rejected));
        var (assetId, _, _, _, _, _) = await SeedAssetAsync(ctx);
        var controller = new AssetMaintenancesController(TestHelpers.BuildMediator(ctx, new FakeScope { Super = true }), ctx, new FakeCurrentUser(), new FakeScope { Super = true }, TestHelpers.CreateActionLogService(ctx));

        var result = await controller.Create(assetId, new CreateAssetMaintenanceRequest(
            AssetMaintenanceType.Repair, "Sửa", null, null,
            StartDate: DateTime.UtcNow.AddDays(5), CompletionDate: DateTime.UtcNow.AddDays(1), Cost: null, IsWarranty: false));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("COMPLETION_BEFORE_START", ReadErrorCode(bad.Value));
    }

    [Fact]
    public async Task UpdateMaintenance_LockedStartDate_RejectedWithFieldLocked()
    {
        await using var ctx = CreateContext(nameof(UpdateMaintenance_LockedStartDate_RejectedWithFieldLocked));
        var (assetId, _, _, _, _, _) = await SeedAssetAsync(ctx);
        var controller = new AssetMaintenancesController(TestHelpers.BuildMediator(ctx, new FakeScope { Super = true }), ctx, new FakeCurrentUser(), new FakeScope { Super = true }, TestHelpers.CreateActionLogService(ctx));
        await controller.Create(assetId, new CreateAssetMaintenanceRequest(
            AssetMaintenanceType.Maintenance, "Bảo trì", null, null, DateTime.UtcNow, null, null, false));
        var m = await ctx.AssetMaintenances.SingleAsync(x => x.AssetId == assetId);

        var result = await controller.Update(m.Id, new UpdateAssetMaintenanceRequest(Title: "Đổi tên", StartDate: DateTime.UtcNow.AddDays(10)));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("FIELD_LOCKED", ReadErrorCode(bad.Value));
    }

    [Fact]
    public async Task UpdateMaintenance_AllowsWhitelistFields()
    {
        await using var ctx = CreateContext(nameof(UpdateMaintenance_AllowsWhitelistFields));
        var (assetId, _, _, _, _, _) = await SeedAssetAsync(ctx);
        var controller = new AssetMaintenancesController(TestHelpers.BuildMediator(ctx, new FakeScope { Super = true }), ctx, new FakeCurrentUser(), new FakeScope { Super = true }, TestHelpers.CreateActionLogService(ctx));
        await controller.Create(assetId, new CreateAssetMaintenanceRequest(
            AssetMaintenanceType.Maintenance, "Bảo trì", null, null, DateTime.UtcNow, null, null, false));
        var m = await ctx.AssetMaintenances.SingleAsync(x => x.AssetId == assetId);

        var result = await controller.Update(m.Id, new UpdateAssetMaintenanceRequest(
            Title: "Bảo trì mở rộng", CompletionDate: DateTime.UtcNow.AddDays(1), Cost: 250m, IsWarranty: true));

        Assert.IsType<OkObjectResult>(result);
        var updated = await ctx.AssetMaintenances.SingleAsync(x => x.Id == m.Id);
        Assert.Equal("Bảo trì mở rộng", updated.Title);
        Assert.NotNull(updated.CompletionDate);
        Assert.Equal(250m, updated.Cost);
        Assert.True(updated.IsWarranty);
        Assert.NotNull(updated.SnapshotSystemInfoId);
        Assert.NotNull(updated.SnapshotSystemPositionId);
    }

    // ==================== DELETE guard ====================

    [Fact]
    public async Task DeleteMaintenance_NonSuperuser_Forbidden()
    {
        await using var ctx = CreateContext(nameof(DeleteMaintenance_NonSuperuser_Forbidden));
        var (assetId, _, _, _, _, _) = await SeedAssetAsync(ctx);
        var controller = new AssetMaintenancesController(TestHelpers.BuildMediator(ctx, new FakeScope { Super = false }), ctx, new FakeCurrentUser(), new FakeScope { Super = false }, TestHelpers.CreateActionLogService(ctx));
        await controller.Create(assetId, new CreateAssetMaintenanceRequest(
            AssetMaintenanceType.Maintenance, "Bảo trì", null, null, DateTime.UtcNow, null, null, false));
        var m = await ctx.AssetMaintenances.SingleAsync(x => x.AssetId == assetId);

        var result = await controller.Delete(m.Id);

        Assert.IsType<ForbidResult>(result);
        Assert.NotNull(await ctx.AssetMaintenances.SingleAsync(x => x.Id == m.Id));
        Assert.Equal(0, await ctx.ActionLogs.CountAsync(l => l.ItemType == ItemType.AssetMaintenance && l.ActionType == ActionType.Delete));
    }

    [Fact]
    public async Task DeleteMaintenance_Superuser_Succeeds_AndLogsContent()
    {
        await using var ctx = CreateContext(nameof(DeleteMaintenance_Superuser_Succeeds_AndLogsContent));
        var (assetId, _, _, _, _, _) = await SeedAssetAsync(ctx);
        var controller = new AssetMaintenancesController(TestHelpers.BuildMediator(ctx, new FakeScope { Super = true }), ctx, new FakeCurrentUser(), new FakeScope { Super = true }, TestHelpers.CreateActionLogService(ctx));
        await controller.Create(assetId, new CreateAssetMaintenanceRequest(
            AssetMaintenanceType.Repair, "Sửa nguồn", "hỏng nguồn", null, DateTime.UtcNow, null, 80m, false));
        var m = await ctx.AssetMaintenances.SingleAsync(x => x.AssetId == assetId);

        var result = await controller.Delete(m.Id);

        Assert.IsType<OkObjectResult>(result);
        var deleted = await ctx.AssetMaintenances.SingleAsync(x => x.Id == m.Id);
        Assert.NotNull(deleted.DeletedAt);
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.AssetMaintenance && l.ActionType == ActionType.Delete);
        Assert.Contains("Sửa nguồn", log.LogMeta);
    }


    // ==================== Company scoping ====================

    private static async Task<Guid> SeedAssetWithCompanyAsync(AppDbContext ctx, Guid companyId)
    {
        var asset = new Asset { AssetTag = $"AST-{Guid.NewGuid().ToString()[..8]}", Name = "Asset", IsConfirmed = true, CompanyId = companyId };
        ctx.Assets.Add(asset);
        await ctx.SaveChangesAsync();
        return asset.Id;
    }

    private static async Task<Guid> SeedMaintenanceAsync(AppDbContext ctx, Guid assetId, Guid companyId, string title)
    {
        var m = new AssetMaintenance
        {
            AssetId = assetId,
            CompanyId = companyId,
            Type = AssetMaintenanceType.Maintenance,
            Title = title,
            StartDate = DateTime.UtcNow,
            CreatedById = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        ctx.AssetMaintenances.Add(m);
        await ctx.SaveChangesAsync();
        return m.Id;
    }

    [Fact]
    public async Task CreateForAsset_SetsCompanyIdFromAsset()
    {
        await using var ctx = CreateContext(nameof(CreateForAsset_SetsCompanyIdFromAsset));
        var company = new Company { Name = "CT-A" };
        ctx.Companies.Add(company);
        await ctx.SaveChangesAsync();
        var assetId = await SeedAssetWithCompanyAsync(ctx, company.Id);
        var controller = new AssetMaintenancesController(TestHelpers.BuildMediator(ctx, new FakeScope { Super = true }), ctx, new FakeCurrentUser(), new FakeScope { Super = true }, TestHelpers.CreateActionLogService(ctx));

        var result = await controller.CreateForAsset(new CreateAssetMaintenanceForAssetRequest(
            assetId, AssetMaintenanceType.Repair, "Sửa", null, null, DateTime.UtcNow, null, 10m, false));

        Assert.IsType<OkObjectResult>(result);
        var m = await ctx.AssetMaintenances.SingleAsync(x => x.AssetId == assetId);
        Assert.Equal(company.Id, m.CompanyId); // server-set, not client-chosen
    }

    [Fact]
    public async Task GetAllMaintenances_RegularUser_SeesOnlyOwnCompany()
    {
        await using var ctx = CreateContext(nameof(GetAllMaintenances_RegularUser_SeesOnlyOwnCompany));
        var companyA = new Company { Name = "CT-A" };
        var companyB = new Company { Name = "CT-B" };
        ctx.Companies.AddRange(companyA, companyB);
        await ctx.SaveChangesAsync();
        var assetA = await SeedAssetWithCompanyAsync(ctx, companyA.Id);
        var assetB = await SeedAssetWithCompanyAsync(ctx, companyB.Id);
        await SeedMaintenanceAsync(ctx, assetA, companyA.Id, "MAINT-A");
        await SeedMaintenanceAsync(ctx, assetB, companyB.Id, "MAINT-B");

        // [Subtask A] Reads drive the Query handlers directly (scope lives in handlers).
        var handler = new ListAllMaintenancesQueryHandler(ctx, new FakeScope { Super = false, CompanyId = companyA.Id });
        var result = await handler.Handle(new ListAllMaintenancesQuery(null, null), CancellationToken.None);

        Assert.Equal(1, result.Items.Count);
        Assert.Equal("MAINT-A", result.Items[0].Title);
    }


    [Fact]
    public async Task GetMaintenance_CrossCompany_Forbidden()
    {
        await using var ctx = CreateContext(nameof(GetMaintenance_CrossCompany_Forbidden));
        var companyA = new Company { Name = "CT-A" };
        var companyB = new Company { Name = "CT-B" };
        ctx.Companies.AddRange(companyA, companyB);
        await ctx.SaveChangesAsync();
        var assetB = await SeedAssetWithCompanyAsync(ctx, companyB.Id);
        var maintenanceId = await SeedMaintenanceAsync(ctx, assetB, companyB.Id, "MAINT-B");

        // [Subtask A] Out-of-scope detail → FORBIDDEN (controller maps to Forbid() 403, verbatim).
        var handler = new GetMaintenanceByIdQueryHandler(ctx, new FakeScope { Super = false, CompanyId = companyA.Id });
        var result = await handler.Handle(new GetMaintenanceByIdQuery(maintenanceId), CancellationToken.None);

        Assert.Equal("FORBIDDEN", result.ErrorCode);
        Assert.Null(result.Detail);
    }

    [Fact]
    public async Task CreateForAsset_CrossCompany_Forbidden()
    {
        await using var ctx = CreateContext(nameof(CreateForAsset_CrossCompany_Forbidden));
        var companyA = new Company { Name = "CT-A" };
        var companyB = new Company { Name = "CT-B" };
        ctx.Companies.AddRange(companyA, companyB);
        await ctx.SaveChangesAsync();
        var assetB = await SeedAssetWithCompanyAsync(ctx, companyB.Id);

        var controller = new AssetMaintenancesController(TestHelpers.BuildMediator(ctx, new FakeScope { Super = false, CompanyId = companyA.Id }), ctx, new FakeCurrentUser(), new FakeScope { Super = false, CompanyId = companyA.Id }, TestHelpers.CreateActionLogService(ctx));
        var result = await controller.CreateForAsset(new CreateAssetMaintenanceForAssetRequest(
            assetB, AssetMaintenanceType.Repair, "Sửa", null, null, DateTime.UtcNow, null, null, false));

        Assert.IsType<ForbidResult>(result); // defense in depth even if the UI filtered the asset list
    }

    [Fact]
    public async Task GetAllMaintenances_Superuser_SeesAllCompanies()
    {
        await using var ctx = CreateContext(nameof(GetAllMaintenances_Superuser_SeesAllCompanies));
        var companyA = new Company { Name = "CT-A" };
        var companyB = new Company { Name = "CT-B" };
        ctx.Companies.AddRange(companyA, companyB);
        await ctx.SaveChangesAsync();
        var assetA = await SeedAssetWithCompanyAsync(ctx, companyA.Id);
        var assetB = await SeedAssetWithCompanyAsync(ctx, companyB.Id);
        await SeedMaintenanceAsync(ctx, assetA, companyA.Id, "MAINT-A");
        await SeedMaintenanceAsync(ctx, assetB, companyB.Id, "MAINT-B");

        var handler = new ListAllMaintenancesQueryHandler(ctx, new FakeScope { Super = true });
        var result = await handler.Handle(new ListAllMaintenancesQuery(null, null), CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task UpdateMaintenance_CannotChangeCompanyId()
    {
        await using var ctx = CreateContext(nameof(UpdateMaintenance_CannotChangeCompanyId));
        var company = new Company { Name = "CT-A" };
        ctx.Companies.Add(company);
        await ctx.SaveChangesAsync();
        var assetId = await SeedAssetWithCompanyAsync(ctx, company.Id);
        var maintenanceId = await SeedMaintenanceAsync(ctx, assetId, company.Id, "MAINT-A");
        var controller = new AssetMaintenancesController(TestHelpers.BuildMediator(ctx, new FakeScope { Super = true }), ctx, new FakeCurrentUser(), new FakeScope { Super = true }, TestHelpers.CreateActionLogService(ctx));

        // CompanyId is not part of the update DTO — a client cannot change it.
        var result = await controller.Update(maintenanceId, new UpdateAssetMaintenanceRequest(Title: "Đổi tên"));

        Assert.IsType<OkObjectResult>(result);
        var m = await ctx.AssetMaintenances.SingleAsync(x => x.Id == maintenanceId);
        Assert.Equal(company.Id, m.CompanyId); // unchanged
    }

    // ==================== Assignees (multi-user, max 5) + Inspection (independent pre-close step) ====================

    private static async Task<(Guid u1, Guid u2, Guid u3, Guid u4, Guid u5, Guid u6)> SeedSixUsersAsync(AppDbContext ctx, Guid companyId)
    {
        var users = new List<User>();
        for (var i = 1; i <= 6; i++)
        {
            users.Add(new User { Username = $"worker{i}", FirstName = $"Worker{i}", LastName = "A", CompanyId = companyId });
        }
        ctx.Users.AddRange(users);
        await ctx.SaveChangesAsync();
        return (users[0].Id, users[1].Id, users[2].Id, users[3].Id, users[4].Id, users[5].Id);
    }

    [Fact]
    public async Task UpdateAssignees_MaxFiveExceeded_ReturnsBadRequest()
    {
        await using var ctx = CreateContext(nameof(UpdateAssignees_MaxFiveExceeded_ReturnsBadRequest));
        var company = new Company { Name = "CT-A" };
        ctx.Companies.Add(company);
        await ctx.SaveChangesAsync();
        var assetId = await SeedAssetWithCompanyAsync(ctx, company.Id);
        var maintenanceId = await SeedMaintenanceAsync(ctx, assetId, company.Id, "MAINT-A");
        var (u1, u2, u3, u4, u5, u6) = await SeedSixUsersAsync(ctx, company.Id);

        var controller = new AssetMaintenancesController(TestHelpers.BuildMediator(ctx, new FakeScope { Super = true }), ctx, new FakeCurrentUser(), new FakeScope { Super = true }, TestHelpers.CreateActionLogService(ctx));
        var result = await controller.Update(maintenanceId,
            new UpdateAssetMaintenanceRequest(AssigneeUserIds: new[] { u1, u2, u3, u4, u5, u6 }));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("MAX_5_ASSIGNEES", ReadErrorCode(bad.Value));
        Assert.Empty(await ctx.AssetMaintenanceAssignees.Where(a => a.MaintenanceId == maintenanceId).ToListAsync());
    }

    [Fact]
    public async Task UpdateAssignees_FiveAccepted_AndListedInDetail()
    {
        await using var ctx = CreateContext(nameof(UpdateAssignees_FiveAccepted_AndListedInDetail));
        var company = new Company { Name = "CT-A" };
        ctx.Companies.Add(company);
        await ctx.SaveChangesAsync();
        var assetId = await SeedAssetWithCompanyAsync(ctx, company.Id);
        var maintenanceId = await SeedMaintenanceAsync(ctx, assetId, company.Id, "MAINT-A");
        var (u1, u2, u3, u4, u5, _) = await SeedSixUsersAsync(ctx, company.Id);

        var controller = new AssetMaintenancesController(TestHelpers.BuildMediator(ctx, new FakeScope { Super = true }), ctx, new FakeCurrentUser(), new FakeScope { Super = true }, TestHelpers.CreateActionLogService(ctx));
        var result = await controller.Update(maintenanceId,
            new UpdateAssetMaintenanceRequest(AssigneeUserIds: new[] { u1, u2, u3, u4, u5 }));
        Assert.IsType<OkObjectResult>(result);

        var rows = await ctx.AssetMaintenanceAssignees.Where(a => a.MaintenanceId == maintenanceId).ToListAsync();
        Assert.Equal(5, rows.Count);
        Assert.Equal(5, rows.Select(r => r.UserId).Distinct().Count());

        // The detail projection must expose the assignees (name + userId + assignedAt).
        var detailHandler = new GetMaintenanceByIdQueryHandler(ctx, new FakeScope { Super = true });
        var detailResult = await detailHandler.Handle(new GetMaintenanceByIdQuery(maintenanceId), CancellationToken.None);
        Assert.Null(detailResult.ErrorCode);
        Assert.Equal(5, detailResult.Detail!.Assignees.Count);
    }

    [Fact]
    public async Task Close_BeforeInspect_ReturnsNotInspectedYet()
    {
        await using var ctx = CreateContext(nameof(Close_BeforeInspect_ReturnsNotInspectedYet));
        var company = new Company { Name = "CT-A" };
        ctx.Companies.Add(company);
        await ctx.SaveChangesAsync();
        var assetId = await SeedAssetWithCompanyAsync(ctx, company.Id);
        var maintenanceId = await SeedMaintenanceAsync(ctx, assetId, company.Id, "MAINT-A");

        // Hoàn thành (CompletionDate set) but NOT inspected → close must be rejected.
        var controller = new AssetMaintenancesController(TestHelpers.BuildMediator(ctx, new FakeScope { Super = true }), ctx, new FakeCurrentUser(), new FakeScope { Super = true }, TestHelpers.CreateActionLogService(ctx));
        await controller.Update(maintenanceId, new UpdateAssetMaintenanceRequest(CompletionDate: DateTime.UtcNow));

        var result = await controller.Close(maintenanceId);
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("MAINTENANCE_NOT_INSPECTED_YET", ReadErrorCode(bad.Value));

        var m = await ctx.AssetMaintenances.SingleAsync(x => x.Id == maintenanceId);
        Assert.False(m.IsClosed);
        Assert.Null(m.ClosedAt);
    }

    [Fact]
    public async Task Inspect_BeforeCompletion_ReturnsNotCompletedYet()
    {
        await using var ctx = CreateContext(nameof(Inspect_BeforeCompletion_ReturnsNotCompletedYet));
        var company = new Company { Name = "CT-A" };
        ctx.Companies.Add(company);
        await ctx.SaveChangesAsync();
        var assetId = await SeedAssetWithCompanyAsync(ctx, company.Id);
        var maintenanceId = await SeedMaintenanceAsync(ctx, assetId, company.Id, "MAINT-A");
        var controller = new AssetMaintenancesController(TestHelpers.BuildMediator(ctx, new FakeScope { Super = true }), ctx, new FakeCurrentUser(), new FakeScope { Super = true }, TestHelpers.CreateActionLogService(ctx));

        var result = await controller.Inspect(maintenanceId);
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("MAINTENANCE_NOT_COMPLETED_YET", ReadErrorCode(bad.Value));

        var m = await ctx.AssetMaintenances.SingleAsync(x => x.Id == maintenanceId);
        Assert.Null(m.InspectedById);
    }

    [Fact]
    public async Task FullFlow_Complete_Inspect_Close_Success_WithActionLogs()
    {
        await using var ctx = CreateContext(nameof(FullFlow_Complete_Inspect_Close_Success_WithActionLogs));
        var company = new Company { Name = "CT-A" };
        ctx.Companies.Add(company);
        await ctx.SaveChangesAsync();
        var assetId = await SeedAssetWithCompanyAsync(ctx, company.Id);
        var maintenanceId = await SeedMaintenanceAsync(ctx, assetId, company.Id, "MAINT-A");
        var currentUser = new FakeCurrentUser();
        var controller = new AssetMaintenancesController(TestHelpers.BuildMediator(ctx, new FakeScope { Super = true }), ctx, currentUser, new FakeScope { Super = true }, TestHelpers.CreateActionLogService(ctx));

        // Step 1 — Hoàn thành (set CompletionDate through the whitelist update).
        var upd = await controller.Update(maintenanceId, new UpdateAssetMaintenanceRequest(CompletionDate: DateTime.UtcNow));
        Assert.IsType<OkObjectResult>(upd);

        // Step 2 — Kiểm tra: independent, repeatable (overwrites InspectedBy/InspectedAt).
        var insp = await controller.Inspect(maintenanceId);
        Assert.IsType<OkObjectResult>(insp);
        var inspAgain = await controller.Inspect(maintenanceId);
        Assert.IsType<OkObjectResult>(inspAgain);

        // Step 3 — Đóng: now allowed (CompletionDate + InspectedById both set).
        var close = await controller.Close(maintenanceId);
        Assert.IsType<OkObjectResult>(close);

        var m = await ctx.AssetMaintenances.SingleAsync(x => x.Id == maintenanceId);
        Assert.True(m.IsClosed);
        Assert.Equal(currentUser.UserId, m.InspectedById);
        Assert.NotNull(m.InspectedAt);
        Assert.Equal(currentUser.UserId, m.ClosedById);

        var actions = await ctx.ActionLogs
            .Where(l => l.ItemId == maintenanceId)
            .Select(l => l.ActionType)
            .ToListAsync();
        Assert.Contains(ActionType.Update, actions);   // set CompletionDate
        Assert.Contains(ActionType.Inspect, actions);
        Assert.Contains(ActionType.Close, actions);
    }

    [Fact]
    public async Task UpdateAssigneesAfterClose_ReturnsMaintenanceClosed()
    {
        await using var ctx = CreateContext(nameof(UpdateAssigneesAfterClose_ReturnsMaintenanceClosed));
        var company = new Company { Name = "CT-A" };
        ctx.Companies.Add(company);
        await ctx.SaveChangesAsync();
        var assetId = await SeedAssetWithCompanyAsync(ctx, company.Id);
        var maintenanceId = await SeedMaintenanceAsync(ctx, assetId, company.Id, "MAINT-A");
        var (u1, _, _, _, _, _) = await SeedSixUsersAsync(ctx, company.Id);
        var controller = new AssetMaintenancesController(TestHelpers.BuildMediator(ctx, new FakeScope { Super = true }), ctx, new FakeCurrentUser(), new FakeScope { Super = true }, TestHelpers.CreateActionLogService(ctx));

        await controller.Update(maintenanceId, new UpdateAssetMaintenanceRequest(CompletionDate: DateTime.UtcNow));
        await controller.Inspect(maintenanceId);
        await controller.Close(maintenanceId);

        // The absolute lock covers the new assignee field as well.
        var result = await controller.Update(maintenanceId, new UpdateAssetMaintenanceRequest(AssigneeUserIds: new[] { u1 }));
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("MAINTENANCE_CLOSED", ReadErrorCode(bad.Value));
    }

    [Fact]
    public async Task Assignees_CrossCompany_RegularUser_ReturnsCompanyMismatch()
    {
        await using var ctx = CreateContext(nameof(Assignees_CrossCompany_RegularUser_ReturnsCompanyMismatch));
        var companyA = new Company { Name = "CT-A" };
        var companyB = new Company { Name = "CT-B" };
        ctx.Companies.AddRange(companyA, companyB);
        await ctx.SaveChangesAsync();
        var assetId = await SeedAssetWithCompanyAsync(ctx, companyA.Id);
        var maintenanceId = await SeedMaintenanceAsync(ctx, assetId, companyA.Id, "MAINT-A");
        var userB = new User { Username = "b1", FirstName = "B", LastName = "One", CompanyId = companyB.Id };
        ctx.Users.Add(userB);
        await ctx.SaveChangesAsync();

        // A regular user of company A may not assign a company-B user to an A-scoped record.
        var controller = new AssetMaintenancesController(TestHelpers.BuildMediator(ctx, new FakeScope { Super = false, CompanyId = companyA.Id }), ctx, new FakeCurrentUser(),
            new FakeScope { Super = false, CompanyId = companyA.Id }, TestHelpers.CreateActionLogService(ctx));
        var result = await controller.Update(maintenanceId, new UpdateAssetMaintenanceRequest(AssigneeUserIds: new[] { userB.Id }));
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("ASSIGNEE_COMPANY_MISMATCH", ReadErrorCode(bad.Value));
    }

    [Fact]
    public async Task Assignees_Superuser_MayAssignUserOfAnyCompany()
    {
        await using var ctx = CreateContext(nameof(Assignees_Superuser_MayAssignUserOfAnyCompany));
        var companyA = new Company { Name = "CT-A" };
        var companyB = new Company { Name = "CT-B" };
        ctx.Companies.AddRange(companyA, companyB);
        await ctx.SaveChangesAsync();
        var assetId = await SeedAssetWithCompanyAsync(ctx, companyA.Id);
        var maintenanceId = await SeedMaintenanceAsync(ctx, assetId, companyA.Id, "MAINT-A");
        var userB = new User { Username = "b1", FirstName = "B", LastName = "One", CompanyId = companyB.Id };
        ctx.Users.Add(userB);
        await ctx.SaveChangesAsync();

        // Superuser is not restricted by company for assignees.
        var controller = new AssetMaintenancesController(TestHelpers.BuildMediator(ctx, new FakeScope { Super = true }), ctx, new FakeCurrentUser(), new FakeScope { Super = true }, TestHelpers.CreateActionLogService(ctx));
        var result = await controller.Update(maintenanceId, new UpdateAssetMaintenanceRequest(AssigneeUserIds: new[] { userB.Id }));
        Assert.IsType<OkObjectResult>(result);
        Assert.Single(await ctx.AssetMaintenanceAssignees.Where(a => a.MaintenanceId == maintenanceId).ToListAsync());
    }

    [Fact]
    public async Task Create_WithAssignees_StoresRows_AndDetailReturnsThem()
    {
        await using var ctx = CreateContext(nameof(Create_WithAssignees_StoresRows_AndDetailReturnsThem));
        var company = new Company { Name = "CT-A" };
        ctx.Companies.Add(company);
        await ctx.SaveChangesAsync();
        var assetId = await SeedAssetWithCompanyAsync(ctx, company.Id);
        var (u1, u2, _, _, _, _) = await SeedSixUsersAsync(ctx, company.Id);
        var currentUser = new FakeCurrentUser();
        var controller = new AssetMaintenancesController(TestHelpers.BuildMediator(ctx, new FakeScope { Super = true }), ctx, currentUser, new FakeScope { Super = true }, TestHelpers.CreateActionLogService(ctx));

        var result = await controller.CreateForAsset(new CreateAssetMaintenanceForAssetRequest(
            assetId, AssetMaintenanceType.Repair, "Sửa", null, null, DateTime.UtcNow, null, null, false, new[] { u1, u2 }));
        var ok = Assert.IsType<OkObjectResult>(result);
        using var createDoc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(ok.Value,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
        var id = Guid.Parse(createDoc.RootElement.GetProperty("data").GetProperty("id").GetString()!);

        Assert.Equal(2, await ctx.AssetMaintenanceAssignees.CountAsync(a => a.MaintenanceId == id));

        var detailHandler = new GetMaintenanceByIdQueryHandler(ctx, new FakeScope { Super = true });
        var detailResult = await detailHandler.Handle(new GetMaintenanceByIdQuery(id), CancellationToken.None);
        Assert.Null(detailResult.ErrorCode);
        Assert.Equal(2, detailResult.Detail!.Assignees.Count);
        Assert.Equal("Worker1 A", detailResult.Detail.Assignees[0].Name);
    }

    // ==================== Close / Reopen (audit-trail lock) ====================

    [Fact]
    public async Task CloseMaintenance_NotCompleted_RejectedWithNotCompletedYet()
    {
        await using var ctx = CreateContext(nameof(CloseMaintenance_NotCompleted_RejectedWithNotCompletedYet));
        var (assetId, _, _, _, _, _) = await SeedAssetAsync(ctx);
        var controller = new AssetMaintenancesController(TestHelpers.BuildMediator(ctx, new FakeScope { Super = true }), ctx, new FakeCurrentUser(), new FakeScope { Super = true }, TestHelpers.CreateActionLogService(ctx));
        await controller.Create(assetId, new CreateAssetMaintenanceRequest(
            AssetMaintenanceType.Maintenance, "Bảo trì", null, null, DateTime.UtcNow, CompletionDate: null, null, false));
        var m = await ctx.AssetMaintenances.SingleAsync(x => x.AssetId == assetId);

        var result = await controller.Close(m.Id);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("MAINTENANCE_NOT_COMPLETED_YET", ReadErrorCode(bad.Value));
        var reloaded = await ctx.AssetMaintenances.SingleAsync(x => x.Id == m.Id);
        Assert.False(reloaded.IsClosed);
    }

    [Fact]
    public async Task CloseMaintenance_Completed_SetsClosedAndLogsClose()
    {
        await using var ctx = CreateContext(nameof(CloseMaintenance_Completed_SetsClosedAndLogsClose));
        var (assetId, _, _, _, _, _) = await SeedAssetAsync(ctx);
        var user = new FakeCurrentUser();
        var controller = new AssetMaintenancesController(TestHelpers.BuildMediator(ctx, new FakeScope { Super = true }), ctx, user, new FakeScope { Super = true }, TestHelpers.CreateActionLogService(ctx));
        await controller.Create(assetId, new CreateAssetMaintenanceRequest(
            AssetMaintenanceType.Repair, "Sửa nguồn", null, null, DateTime.UtcNow, DateTime.UtcNow.AddDays(1), 50m, false));
        var m = await ctx.AssetMaintenances.SingleAsync(x => x.AssetId == assetId);

        await controller.Inspect(m.Id); // inspection is a required pre-close step (workflow 3 bước)
        var result = await controller.Close(m.Id);

        Assert.IsType<OkObjectResult>(result);
        var reloaded = await ctx.AssetMaintenances.SingleAsync(x => x.Id == m.Id);
        Assert.True(reloaded.IsClosed);
        Assert.NotNull(reloaded.ClosedAt);
        Assert.Equal(user.UserId, reloaded.ClosedById);
        Assert.Equal(1, await ctx.ActionLogs.CountAsync(l => l.ItemType == ItemType.AssetMaintenance && l.ActionType == ActionType.Close));
    }

    [Fact]
    public async Task UpdateMaintenance_ClosedRecord_RejectedWithClosed()
    {
        await using var ctx = CreateContext(nameof(UpdateMaintenance_ClosedRecord_RejectedWithClosed));
        var (assetId, _, _, _, _, _) = await SeedAssetAsync(ctx);
        var controller = new AssetMaintenancesController(TestHelpers.BuildMediator(ctx, new FakeScope { Super = true }), ctx, new FakeCurrentUser(), new FakeScope { Super = true }, TestHelpers.CreateActionLogService(ctx));
        await controller.Create(assetId, new CreateAssetMaintenanceRequest(
            AssetMaintenanceType.Maintenance, "Bảo trì", null, null, DateTime.UtcNow, DateTime.UtcNow.AddDays(1), null, false));
        var m = await ctx.AssetMaintenances.SingleAsync(x => x.AssetId == assetId);
        await controller.Inspect(m.Id); // required pre-close step
        await controller.Close(m.Id);

        // Even a perfectly valid whitelist update is rejected once the record is closed.
        var result = await controller.Update(m.Id, new UpdateAssetMaintenanceRequest(Title: "Đổi tên sau khi đóng"));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("MAINTENANCE_CLOSED", ReadErrorCode(bad.Value));
    }

    [Fact]
    public async Task ReopenMaintenance_RegularUser_Forbidden()
    {
        await using var ctx = CreateContext(nameof(ReopenMaintenance_RegularUser_Forbidden));
        var (assetId, _, _, _, _, _) = await SeedAssetAsync(ctx);
        var controller = new AssetMaintenancesController(TestHelpers.BuildMediator(ctx, new FakeScope { Super = true }), ctx, new FakeCurrentUser(), new FakeScope { Super = true }, TestHelpers.CreateActionLogService(ctx));
        await controller.Create(assetId, new CreateAssetMaintenanceRequest(
            AssetMaintenanceType.Maintenance, "Bảo trì", null, null, DateTime.UtcNow, DateTime.UtcNow.AddDays(1), null, false));
        var m = await ctx.AssetMaintenances.SingleAsync(x => x.AssetId == assetId);
        await controller.Inspect(m.Id); // required pre-close step
        await controller.Close(m.Id);

        var regular = new AssetMaintenancesController(TestHelpers.BuildMediator(ctx, new FakeScope { Super = false }), ctx, new FakeCurrentUser(), new FakeScope { Super = false }, TestHelpers.CreateActionLogService(ctx));
        var result = await regular.Reopen(m.Id);

        Assert.IsType<ForbidResult>(result);
        Assert.True((await ctx.AssetMaintenances.SingleAsync(x => x.Id == m.Id)).IsClosed);
    }

    [Fact]
    public async Task ReopenMaintenance_Superuser_Succeeds_AndLogsReopen()
    {
        await using var ctx = CreateContext(nameof(ReopenMaintenance_Superuser_Succeeds_AndLogsReopen));
        var (assetId, _, _, _, _, _) = await SeedAssetAsync(ctx);
        var controller = new AssetMaintenancesController(TestHelpers.BuildMediator(ctx, new FakeScope { Super = true }), ctx, new FakeCurrentUser(), new FakeScope { Super = true }, TestHelpers.CreateActionLogService(ctx));
        await controller.Create(assetId, new CreateAssetMaintenanceRequest(
            AssetMaintenanceType.Maintenance, "Bảo trì", null, null, DateTime.UtcNow, DateTime.UtcNow.AddDays(1), null, false));
        var m = await ctx.AssetMaintenances.SingleAsync(x => x.AssetId == assetId);
        await controller.Inspect(m.Id); // required pre-close step
        await controller.Close(m.Id);
        var closedAt = (await ctx.AssetMaintenances.SingleAsync(x => x.Id == m.Id)).ClosedAt;

        var result = await controller.Reopen(m.Id);

        Assert.IsType<OkObjectResult>(result);
        var reloaded = await ctx.AssetMaintenances.SingleAsync(x => x.Id == m.Id);
        Assert.False(reloaded.IsClosed);
        Assert.Equal(closedAt, reloaded.ClosedAt); // kept as the most-recent-close history
        Assert.Equal(1, await ctx.ActionLogs.CountAsync(l => l.ItemType == ItemType.AssetMaintenance && l.ActionType == ActionType.Reopen));
    }

    [Fact]
    public async Task GetMaintenance_CurrentContext_ReflectsLiveAsset_NotSnapshot()
    {
        await using var ctx = CreateContext(nameof(GetMaintenance_CurrentContext_ReflectsLiveAsset_NotSnapshot));
        var (assetId, sysInfo, pos, loc, dept, user) = await SeedAssetAsync(ctx);
        var controller = new AssetMaintenancesController(TestHelpers.BuildMediator(ctx, new FakeScope { Super = true }), ctx, new FakeCurrentUser(), new FakeScope { Super = true }, TestHelpers.CreateActionLogService(ctx));
        await controller.Create(assetId, new CreateAssetMaintenanceRequest(
            AssetMaintenanceType.Maintenance, "Bảo trì", null, null, DateTime.UtcNow, DateTime.UtcNow.AddDays(1), null, false));
        var m = await ctx.AssetMaintenances.SingleAsync(x => x.AssetId == assetId);

        // Move the asset NOW: different system, position, location and assigned user/department.
        var newSys = new SystemInfo { Name = "Hệ thống B" };
        var newPos = new SystemPosition { Name = "Vị trí 9", SystemInfo = newSys };
        var newLoc = new Location { Name = "Kho B" };
        var newDept = new Department { Name = "Phòng Kỹ thuật" };
        var newUser = new User { Username = "user2", FirstName = "Tran", LastName = "B", Department = newDept };
        ctx.SystemInfos.Add(newSys);
        ctx.SystemPositions.Add(newPos);
        ctx.Locations.Add(newLoc);
        ctx.Departments.Add(newDept);
        ctx.Users.Add(newUser);
        await ctx.SaveChangesAsync();
        var asset = await ctx.Assets.SingleAsync(a => a.Id == assetId);
        asset.SystemPositionId = newPos.Id;
        asset.LocationId = newLoc.Id;
        var newAsgn = new Assignment
        {
            AssetId = asset.Id,
            TargetType = AssignmentTargetType.User,
            TargetId = newUser.Id,
            AssignedById = Guid.NewGuid()
        };
        ctx.Assignments.Add(newAsgn);
        await ctx.SaveChangesAsync();
        asset.CurrentAssignmentId = newAsgn.Id;
        await ctx.SaveChangesAsync();

        var detailHandler = new GetMaintenanceByIdQueryHandler(ctx, new FakeScope { Super = true });
        var detailResult = await detailHandler.Handle(new GetMaintenanceByIdQuery(m.Id), CancellationToken.None);
        Assert.Null(detailResult.ErrorCode);
        var detail = detailResult.Detail!;
        var cc = detail.CurrentContext;

        // Snapshot* stays frozen at creation time.
        Assert.Equal(sysInfo.Id, detail.SnapshotSystemInfoId);
        Assert.Equal(pos.Id, detail.SnapshotSystemPositionId);
        Assert.Equal(loc.Id, detail.SnapshotLocationId);
        Assert.Equal(user.Id, detail.SnapshotAssignedUserId);
        Assert.Equal(dept.Id, detail.SnapshotDepartmentId);
        // currentContext reflects the LIVE state of the asset (computed on the fly).
        Assert.Equal(newSys.Id, cc.SystemInfoId);
        Assert.Equal(newPos.Id, cc.SystemPositionId);
        Assert.Equal(newLoc.Id, cc.LocationId);
        Assert.Equal(newUser.Id, cc.AssignedUserId);
        Assert.Equal(newDept.Id, cc.DepartmentId);
    }

    private static string ReadErrorCode(object? value)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(value, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
        return doc.RootElement.GetProperty("error_code").GetString()!;
    }
}
