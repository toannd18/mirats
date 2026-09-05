using System.Text.Json;
using aspire_react.Server.Application.ActionLogs.Queries;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// MC-3 — MaintenanceCampaignsController: create + auto device snapshot (immutable), checklist
/// results upsert (patch-aware, blocked after Complete), complete (Status + NextMaintenanceDueDate
/// = EndDate + min CycleMonths theo quyết định đã chốt), campaign ActionLogs, và filter mở rộng
/// GetBySystem (hướng b: ItemType == MaintenanceCampaign).
/// </summary>
public class MaintenanceCampaignTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static string? ErrorCode(IActionResult result)
    {
        object? value = result switch
        {
            BadRequestObjectResult bad => bad.Value,
            NotFoundObjectResult nf => nf.Value,
            _ => null
        };
        if (value == null) return null;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value, WebJson));
        return doc.RootElement.TryGetProperty("error_code", out var el) ? el.GetString() : null;
    }

    private sealed record Fixture(
        Company Company,
        SystemInfo System,
        SystemPosition Pos1,
        SystemPosition Pos2,
        Asset AssetA,
        Asset AssetB,
        Asset AssetFree,
        MaintenanceChecklistTemplate Template,
        MaintenanceChecklistTemplateVersion Version,
        Guid UserId);

    /// <summary>Company-owned system + 2 positions + 2 mounted assets + 1 free asset + published current version.</summary>
    private static async Task<Fixture> SeedAsync(AppDbContext db)
    {
        var company = new Company { Code = "MC3B", Name = "Công ty MC3" };
        var sys = new SystemInfo { Code = "SYS-2026-777", Name = "Hệ thống MC3", CompanyId = company.Id, NextMaintenanceDueDate = null };
        var pos1 = new SystemPosition { Code = "POS-2026-001", Name = "Vị trí 1", SystemInfoId = sys.Id };
        var pos2 = new SystemPosition { Code = "POS-2026-002", Name = "Vị trí 2", SystemInfoId = sys.Id };
        var model = new AssetModel { Name = "Model X", ModelNumber = "MX-100" };
        var assetA = new Asset { AssetTag = "AST-001", Name = "Server A", Serial = "SN-AAA", ModelId = model.Id, SystemPositionId = pos1.Id };
        var assetB = new Asset { AssetTag = "AST-002", Name = "Switch B", Serial = "SN-BBB", SystemPositionId = pos2.Id };
        var assetFree = new Asset { AssetTag = "AST-003", Name = "Laptop C", Serial = "SN-CCC", SystemPositionId = null };
        var user = new User { Username = "mc3-user", FirstName = "MC3", LastName = "User" };

        var template = new MaintenanceChecklistTemplate { Name = "T-MC3", SystemInfoId = sys.Id, CompanyId = company.Id, CreatedById = user.Id };
        var version = new MaintenanceChecklistTemplateVersion
        {
            TemplateId = template.Id,
            VersionNumber = 1,
            PublishedAt = DateTime.UtcNow,
            IsCurrent = true,
            CreatedById = user.Id
        };
        version.Items.Add(new MaintenanceChecklistItem { Order = 1, Name = "Kiểm tra nguồn", CycleMonths = 6 });
        version.Items.Add(new MaintenanceChecklistItem { Order = 2, Name = "Vệ sinh quạt", CycleMonths = 3 });
        template.Versions.Add(version);

        db.Companies.Add(company);
        db.SystemInfos.Add(sys);
        db.SystemPositions.AddRange(pos1, pos2);
        db.Models.Add(model);
        db.Assets.AddRange(assetA, assetB, assetFree);
        db.Users.Add(user);
        db.MaintenanceChecklistTemplates.Add(template);
        await db.SaveChangesAsync();

        return new Fixture(company, sys, pos1, pos2, assetA, assetB, assetFree, template, version, user.Id);
    }

    private static MaintenanceCampaignsController Ctx(AppDbContext db, bool super, Guid? companyId)
        => new(TestHelpers.BuildMediator(db, new TestHelpers.FakeScope { Super = super, CompanyId = companyId }),
            db, new TestHelpers.FakeCurrentUser(), new TestHelpers.FakeScope { Super = super, CompanyId = companyId },
            TestHelpers.CreateActionLogService(db));

    // ==================== Create + snapshot ====================

    [Fact]
    public async Task Create_SnapshotsAllMountedAssets_WithPinnedCurrentVersion()
    {
        await using var db = TestHelpers.CreateContext(nameof(Create_SnapshotsAllMountedAssets_WithPinnedCurrentVersion));
        var fx = await SeedAsync(db);
        var controller = Ctx(db, true, null);

        var result = await controller.Create(new CreateCampaignRequest(fx.System.Id, null, DateTime.UtcNow, null, "DOT-01", null));

        var ok = Assert.IsType<OkObjectResult>(result);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(2, data.GetProperty("snapshotCount").GetInt32());

        var campaign = await db.MaintenanceCampaigns.Include(c => c.DeviceSnapshots).SingleAsync();
        Assert.Equal(fx.Version.Id, campaign.TemplateVersionId);
        Assert.Equal(MaintenanceCampaignStatus.InProgress, campaign.Status);
        Assert.Equal(fx.Company.Id, campaign.CompanyId);
        var snaps = campaign.DeviceSnapshots.OrderBy(s => s.AssetTag).ToList();

        Assert.Equal(2, snaps.Count);
        Assert.Equal(fx.AssetA.Id, snaps[0].AssetId);
        Assert.Equal("AST-001", snaps[0].AssetTag);
        Assert.Equal("Server A", snaps[0].AssetName);
        Assert.Equal("SN-AAA", snaps[0].Serial);
        Assert.Equal("MX-100", snaps[0].ModelNumber);
        Assert.Equal(fx.Pos1.Id, snaps[0].SystemPositionId);
        Assert.Equal("Vị trí 1", snaps[0].SystemPositionName);
        Assert.Equal(fx.AssetB.Id, snaps[1].AssetId);
        // Free asset (no position) must NOT be snapshotted.
        Assert.DoesNotContain(snaps, s => s.AssetId == fx.AssetFree.Id);

        Assert.True(await db.ActionLogs.AnyAsync(l =>
            l.ItemType == ItemType.MaintenanceCampaign && l.ActionType == ActionType.Create && l.ItemId == campaign.Id));
    }

    [Fact]
    public async Task Create_NoCurrentPublishedVersion_400()
    {
        await using var db = TestHelpers.CreateContext(nameof(Create_NoCurrentPublishedVersion_400));
        var fx = await SeedAsync(db);
        // Demote + unpublish so the template has NO current/published version.
        var v = await db.MaintenanceChecklistTemplateVersions.SingleAsync(x => x.Id == fx.Version.Id);
        v.IsCurrent = false;
        v.PublishedAt = null;
        await db.SaveChangesAsync();

        var controller = Ctx(db, true, null);
        var result = await controller.Create(new CreateCampaignRequest(fx.System.Id, null, DateTime.UtcNow, null, null, null));

        Assert.Equal("NO_CURRENT_VERSION", ErrorCode(result));
    }

    [Fact]
    public async Task Create_SystemOutOfScope_404HidesExistence()
    {
        await using var db = TestHelpers.CreateContext(nameof(Create_SystemOutOfScope_404HidesExistence));
        var fx = await SeedAsync(db); // system belongs to fx.Company
        var other = new Company { Code = "OTHER", Name = "Công ty khác" };
        db.Companies.Add(other);
        await db.SaveChangesAsync();

        var controller = Ctx(db, false, other.Id);
        var result = await controller.Create(new CreateCampaignRequest(fx.System.Id, null, DateTime.UtcNow, null, null, null));

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Create_SecondInProgressCampaign_Blocked()
    {
        await using var db = TestHelpers.CreateContext(nameof(Create_SecondInProgressCampaign_Blocked));
        var fx = await SeedAsync(db);
        var controller = Ctx(db, true, null);

        Assert.IsType<OkObjectResult>(await controller.Create(new CreateCampaignRequest(fx.System.Id, null, DateTime.UtcNow, null, "DOT-01", null)));
        var second = await controller.Create(new CreateCampaignRequest(fx.System.Id, null, DateTime.UtcNow.AddDays(1), null, "DOT-02", null));

        Assert.Equal("CAMPAIGN_ALREADY_IN_PROGRESS", ErrorCode(second));
    }

    [Fact]
    public async Task Create_TemplateOfAnotherSystem_400()
    {
        await using var db = TestHelpers.CreateContext(nameof(Create_TemplateOfAnotherSystem_400));
        var fx = await SeedAsync(db);
        // A template pinned to a DIFFERENT system.
        var otherSys = new SystemInfo { Code = "SYS-2026-778", Name = "Hệ thống khác", CompanyId = fx.Company.Id };
        var otherTpl = new MaintenanceChecklistTemplate { Name = "T-Khac", SystemInfoId = otherSys.Id, CompanyId = fx.Company.Id, CreatedById = fx.UserId };
        var otherVer = new MaintenanceChecklistTemplateVersion { TemplateId = otherTpl.Id, VersionNumber = 1, PublishedAt = DateTime.UtcNow, IsCurrent = true, CreatedById = fx.UserId };
        otherTpl.Versions.Add(otherVer);
        db.SystemInfos.Add(otherSys);
        db.MaintenanceChecklistTemplates.Add(otherTpl);
        await db.SaveChangesAsync();

        var controller = Ctx(db, true, null);
        var result = await controller.Create(new CreateCampaignRequest(fx.System.Id, otherTpl.Id, DateTime.UtcNow, null, null, null));

        Assert.Equal("TEMPLATE_SYSTEM_MISMATCH", ErrorCode(result));
    }

    // ==================== Results ====================

    private static async Task<Guid> CreateCampaignAsync(AppDbContext db, Fixture fx)
    {
        var c = new MaintenanceCampaign
        {
            SystemInfoId = fx.System.Id,
            TemplateVersionId = fx.Version.Id,
            StartDate = DateTime.UtcNow,
            BatchNumber = "DOT-01",
            CompanyId = fx.Company.Id
        };
        c.DeviceSnapshots.Add(new MaintenanceCampaignDeviceSnapshot
        {
            AssetId = fx.AssetA.Id, AssetTag = "AST-001", AssetName = "Server A", Serial = "SN-AAA",
            SystemPositionId = fx.Pos1.Id, SystemPositionName = "Vị trí 1"
        });
        c.DeviceSnapshots.Add(new MaintenanceCampaignDeviceSnapshot
        {
            AssetId = fx.AssetB.Id, AssetTag = "AST-002", AssetName = "Switch B", Serial = "SN-BBB",
            SystemPositionId = fx.Pos2.Id, SystemPositionName = "Vị trí 2"
        });
        db.MaintenanceCampaigns.Add(c);
        await db.SaveChangesAsync();
        return c.Id;
    }

    [Fact]
    public async Task UpsertResult_PatchAware_UpdatesExistingRow()
    {
        await using var db = TestHelpers.CreateContext(nameof(UpsertResult_PatchAware_UpdatesExistingRow));
        var fx = await SeedAsync(db);
        var campaignId = await CreateCampaignAsync(db, fx);
        var controller = Ctx(db, true, null);

        var item1 = fx.Version.Items.OrderBy(i => i.Order).First();
        var snapshot = await db.MaintenanceCampaignDeviceSnapshots.FirstAsync(s => s.CampaignId == campaignId);

        var created = await controller.UpsertResult(campaignId, new UpsertCampaignResultDto(snapshot.Id, item1.Id, "220V", true, "OK mức 1"));
        Assert.IsType<OkObjectResult>(created);

        // PATCH: only Notes sent → MeasuredValue + IsPass untouched.
        var updated = await controller.UpsertResult(campaignId, new UpsertCampaignResultDto(snapshot.Id, item1.Id, null, null, "Cập nhật ghi chú"));
        Assert.IsType<OkObjectResult>(updated);

        var row = await db.MaintenanceChecklistResults.SingleAsync();
        Assert.Equal("220V", row.MeasuredValue);
        Assert.True(row.IsPass);
        Assert.Equal("Cập nhật ghi chú", row.Notes);
        Assert.Equal(1, await db.MaintenanceChecklistResults.CountAsync());
    }

    [Fact]
    public async Task UpsertResult_InvalidItemOrSnapshot_400()
    {
        await using var db = TestHelpers.CreateContext(nameof(UpsertResult_InvalidItemOrSnapshot_400));
        var fx = await SeedAsync(db);
        var campaignId = await CreateCampaignAsync(db, fx);
        var controller = Ctx(db, true, null);

        // Item from a DIFFERENT template version.
        var rogueItem = new MaintenanceChecklistItem { Order = 9, Name = "Rogue", CycleMonths = 12 };
        db.MaintenanceChecklistItems.Add(rogueItem);

        // Snapshot from a DIFFERENT campaign.
        var otherCampaign = await CreateCampaignAsync(db, fx);
        var otherSnap = await db.MaintenanceCampaignDeviceSnapshots.FirstAsync(s => s.CampaignId == otherCampaign);
        // A legit snapshot of THIS campaign + legit item (for the isolation matrix below).
        var legitSnap = await db.MaintenanceCampaignDeviceSnapshots.FirstAsync(s => s.CampaignId == campaignId);
        var legitItem = fx.Version.Items.OrderBy(i => i.Order).First();

        await db.SaveChangesAsync();

        // Valid snapshot + rogue item → item check fails.
        Assert.Equal("INVALID_CHECKLIST_ITEM",
            ErrorCode(await controller.UpsertResult(campaignId, new UpsertCampaignResultDto(legitSnap.Id, rogueItem.Id, null, true, null))));
        // Rogue snapshot + valid item → snapshot check fails (checked first in the controller).
        Assert.Equal("INVALID_DEVICE_SNAPSHOT",
            ErrorCode(await controller.UpsertResult(campaignId, new UpsertCampaignResultDto(otherSnap.Id, legitItem.Id, null, true, null))));
        // Both valid → success.
        Assert.IsType<OkObjectResult>(
            await controller.UpsertResult(campaignId, new UpsertCampaignResultDto(legitSnap.Id, legitItem.Id, "OK", true, null)));
    }

    [Fact]
    public async Task Result_AfterComplete_400()
    {
        await using var db = TestHelpers.CreateContext(nameof(Result_AfterComplete_400));
        var fx = await SeedAsync(db);
        var campaignId = await CreateCampaignAsync(db, fx);
        var c = await db.MaintenanceCampaigns.FindAsync(campaignId);
        c!.Status = MaintenanceCampaignStatus.Completed;
        c.EndDate = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var controller = Ctx(db, true, null);

        var snap = await db.MaintenanceCampaignDeviceSnapshots.FirstAsync(s => s.CampaignId == campaignId);
        var item = fx.Version.Items.First();

        Assert.Equal("CAMPAIGN_COMPLETED",
            ErrorCode(await controller.UpsertResult(campaignId, new UpsertCampaignResultDto(snap.Id, item.Id, "1", true, null))));
        Assert.Equal("CAMPAIGN_COMPLETED",
            ErrorCode(await controller.DeleteResult(campaignId, new DeleteCampaignResultDto(snap.Id, item.Id))));
    }

    // ==================== Complete ====================

    [Fact]
    public async Task Complete_RequiresAllResults_ThenSetsStatusAndDueDate()
    {
        await using var db = TestHelpers.CreateContext(nameof(Complete_RequiresAllResults_ThenSetsStatusAndDueDate));
        var fx = await SeedAsync(db);
        var campaignId = await CreateCampaignAsync(db, fx);
        var controller = Ctx(db, true, null);

        // 2 snapshots × 2 items = 4 expected; only record 1 → blocked.
        var snap1 = (await db.MaintenanceCampaignDeviceSnapshots.Where(s => s.CampaignId == campaignId).OrderBy(s => s.AssetTag).ToListAsync())[0];
        var item1 = fx.Version.Items.OrderBy(i => i.Order).First();
        await controller.UpsertResult(campaignId, new UpsertCampaignResultDto(snap1.Id, item1.Id, "OK", true, null));

        var incomplete = await controller.Complete(campaignId);
        Assert.Equal("CAMPAIGN_RESULTS_INCOMPLETE", ErrorCode(incomplete));

        // Fill the rest (loop upsert all combos).
        var snaps = await db.MaintenanceCampaignDeviceSnapshots.Where(s => s.CampaignId == campaignId).ToListAsync();
        foreach (var s in snaps)
            foreach (var it in fx.Version.Items)
                await controller.UpsertResult(campaignId, new UpsertCampaignResultDto(s.Id, it.Id, "OK", true, "done"));

        var endDate = DateTime.UtcNow.AddDays(-1); // explicit past EndDate to make the math exact
        var campaignRow = await db.MaintenanceCampaigns.FindAsync(campaignId);
        campaignRow!.EndDate = endDate;
        await db.SaveChangesAsync();

        var completed = await controller.Complete(campaignId);
        Assert.IsType<OkObjectResult>(completed);

        var c = await db.MaintenanceCampaigns.FindAsync(campaignId);
        Assert.Equal(MaintenanceCampaignStatus.Completed, c!.Status);
        Assert.Equal(endDate, c.EndDate);

        // min(CycleMonths) = 3 (item "Vệ sinh quạt") → due = EndDate + 3 months.
        var sys = await db.SystemInfos.FindAsync(fx.System.Id);
        Assert.NotNull(sys!.NextMaintenanceDueDate);
        Assert.Equal(endDate.AddMonths(3), sys.NextMaintenanceDueDate.Value);

        Assert.True(await db.ActionLogs.AnyAsync(l =>
            l.ItemType == ItemType.MaintenanceCampaign && l.ActionType == ActionType.Complete && l.ItemId == campaignId));
    }

    [Fact]
    public async Task Complete_AlreadyCompleted_400()
    {
        await using var db = TestHelpers.CreateContext(nameof(Complete_AlreadyCompleted_400));
        var fx = await SeedAsync(db);
        var campaignId = await CreateCampaignAsync(db, fx);
        var c = await db.MaintenanceCampaigns.FindAsync(campaignId);
        c!.Status = MaintenanceCampaignStatus.Completed;
        c.EndDate = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var controller = Ctx(db, true, null);
        Assert.Equal("CAMPAIGN_ALREADY_COMPLETED", ErrorCode(await controller.Complete(campaignId)));
    }

    [Fact]
    public async Task Snapshot_Immutable_AfterAssetsDetached()
    {
        await using var db = TestHelpers.CreateContext(nameof(Snapshot_Immutable_AfterAssetsDetached));
        var fx = await SeedAsync(db);
        var campaignId = await CreateCampaignAsync(db, fx);

        var before = await db.MaintenanceCampaignDeviceSnapshots
            .Where(s => s.CampaignId == campaignId)
            .Select(s => new { s.AssetId, s.AssetTag, s.AssetName, s.SystemPositionName })
            .ToListAsync();

        // Move asset A OUT of its position (and even rename it) — the snapshot must NOT change.
        var assetA = await db.Assets.FindAsync(fx.AssetA.Id);
        assetA!.SystemPositionId = null;
        assetA.Name = "Server A (đã tháo)";
        var pos1 = await db.SystemPositions.FindAsync(fx.Pos1.Id);
        pos1!.Name = "Vị trí 1 (đã đổi tên)";
        await db.SaveChangesAsync();

        var after = await db.MaintenanceCampaignDeviceSnapshots
            .Where(s => s.CampaignId == campaignId)
            .Select(s => new { s.AssetId, s.AssetTag, s.AssetName, s.SystemPositionName })
            .ToListAsync();

        Assert.Equal(before.Count, after.Count);
        // Snapshot keeps the ORIGINAL capture-time values for the moved/renamed asset.
        Assert.Contains(after, s => s.AssetId == fx.AssetA.Id && s.AssetTag == "AST-001" && s.AssetName == "Server A" && s.SystemPositionName == "Vị trí 1");
    }

    // ==================== Executors (MC-6) ====================

    [Fact]
    public async Task Create_PersistsExecutors_AndValidatesCompany()
    {
        await using var db = TestHelpers.CreateContext(nameof(Create_PersistsExecutors_AndValidatesCompany));
        var fx = await SeedAsync(db);
        var mate1 = new User { Username = "mc6-mate1", FirstName = "Mate", LastName = "One", CompanyId = fx.Company.Id };
        var mate2 = new User { Username = "mc6-mate2", FirstName = "Mate", LastName = "Two", CompanyId = fx.Company.Id };
        var foreign = new User { Username = "mc6-foreign", FirstName = "For", LastName = "Eign", CompanyId = null };
        db.Users.AddRange(mate1, mate2, foreign);
        await db.SaveChangesAsync();

        var controller = Ctx(db, false, fx.Company.Id); // regular user of the company

        // Nonexistent user → INVALID_EXECUTOR.
        var badUser = await controller.Create(new CreateCampaignRequest(
            fx.System.Id, null, DateTime.UtcNow, null, "D1", null, new[] { Guid.NewGuid() }));
        Assert.Equal("INVALID_EXECUTOR", ErrorCode(badUser));

        // Foreign-company user → EXECUTOR_COMPANY_MISMATCH.
        var foreignResult = await controller.Create(new CreateCampaignRequest(
            fx.System.Id, null, DateTime.UtcNow, null, "D1", null, new[] { mate1.Id, foreign.Id }));
        Assert.Equal("EXECUTOR_COMPANY_MISMATCH", ErrorCode(foreignResult));

        // Valid executors → persisted + deduped.
        var ok = Assert.IsType<OkObjectResult>(await controller.Create(new CreateCampaignRequest(
            fx.System.Id, null, DateTime.UtcNow, null, "D-OK", null, new[] { mate1.Id, mate2.Id, mate1.Id })));
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        Assert.Equal(2, doc.RootElement.GetProperty("data").GetProperty("executorCount").GetInt32());

        var campaign = await db.MaintenanceCampaigns.Include(c => c.Executors).FirstAsync(c => c.BatchNumber == "D-OK");
        Assert.Equal(2, campaign.Executors.Count);
    }

    // ==================== GetBySystem expansion (hướng b) ====================

    [Fact]
    public async Task GetBySystem_IncludesCampaignLogs_SideBySideWithPositionLogs()
    {
        await using var db = TestHelpers.CreateContext(nameof(GetBySystem_IncludesCampaignLogs_SideBySideWithPositionLogs));
        var fx = await SeedAsync(db);
        var campaignId = await CreateCampaignAsync(db, fx);
        var creator = fx.UserId;

        // Campaign events (Create + Complete) with TargetSystemInfoId — the MC-3 expansion.
        db.ActionLogs.Add(new ActionLog
        {
            ItemType = ItemType.MaintenanceCampaign, ItemId = campaignId, ActionType = ActionType.Create,
            CreatedBy = creator, CompanyId = fx.Company.Id,
            TargetSystemInfoId = fx.System.Id, TargetSystemInfoName = fx.System.Name,
            Note = "Tạo đợt bảo dưỡng"
        });
        db.ActionLogs.Add(new ActionLog
        {
            ItemType = ItemType.MaintenanceCampaign, ItemId = campaignId, ActionType = ActionType.Complete,
            CreatedBy = creator, CompanyId = fx.Company.Id,
            TargetSystemInfoId = fx.System.Id, TargetSystemInfoName = fx.System.Name,
            Note = "Hoàn thành đợt bảo dưỡng"
        });
        // Existing system-position asset event (the original filter class).
        db.ActionLogs.Add(new ActionLog
        {
            ItemType = ItemType.Asset, ItemId = fx.AssetA.Id, ActionType = ActionType.Checkout,
            CreatedBy = creator, CompanyId = fx.Company.Id,
            TargetType = AssignmentTargetType.SystemPosition, TargetId = fx.Pos1.Id,
            TargetSystemInfoId = fx.System.Id, TargetSystemInfoName = fx.System.Name,
            Note = "Lắp đặt vào vị trí 1"
        });
        // Noise: a campaign log of ANOTHER system must stay filtered out.
        var otherSys = new SystemInfo { Code = "SYS-2026-779", Name = "Hệ thống khác", CompanyId = fx.Company.Id };
        var otherCampaign = await CreateCampaignAsync(db, fx);
        db.ActionLogs.Add(new ActionLog
        {
            ItemType = ItemType.MaintenanceCampaign, ItemId = otherCampaign, ActionType = ActionType.Create,
            CreatedBy = creator, CompanyId = fx.Company.Id,
            TargetSystemInfoId = otherSys.Id, TargetSystemInfoName = otherSys.Name, Note = "Khác hệ thống"
        });
        await db.SaveChangesAsync();

        // [Giai đoạn 3] ActionLogs migrated to MediatR — drive GetBySystemQueryHandler directly
        // (real company-scope via TestHelpers.FakeScope superuser; same filter/paging substance).
        var handler = new GetBySystemQueryHandler(db, new TestHelpers.FakeScope { Super = true });
        var page = await handler.Handle(
            new GetBySystemQuery(fx.System.Id, PageSize: 50), CancellationToken.None);

        Assert.NotNull(page);
        var rows = page!.Items;

        var itemTypes = rows.Select(r => r.ItemType).ToList();

        Assert.Contains("MaintenanceCampaign", itemTypes);
        Assert.Contains("Asset", itemTypes);
        // Both campaign events present (Create + Complete).
        var campaignRows = rows.Where(r => r.ItemType == "MaintenanceCampaign").ToList();
        Assert.Equal(2, campaignRows.Count);
        Assert.All(campaignRows, r => Assert.Equal(fx.System.Id, r.TargetSystemInfoId));
        // Campaign display name resolved from the campaign table.
        Assert.Contains(campaignRows, r => r.ItemName is not null);
        // Noise (other system) excluded.
        Assert.Equal(3, rows.Count);
        Assert.Equal(3, page.Total);
    }

    // ==================== MC-7c: applicable pairs (phạm vi vị trí) ====================

    /// <summary>Scoped item1 → [Pos1]; item2 stays universal. Returns (itemScoped, itemUniversal).</summary>
    private static async Task<(MaintenanceChecklistItem scoped, MaintenanceChecklistItem universal)>
        ScopeItem1ToPos1Async(AppDbContext db, Fixture fx)
    {
        var item1 = fx.Version.Items.OrderBy(i => i.Order).First();
        var item2 = fx.Version.Items.OrderBy(i => i.Order).Last();
        db.MaintenanceChecklistItemPositions.Add(new MaintenanceChecklistItemPosition { ItemId = item1.Id, SystemPositionId = fx.Pos1.Id });
        await db.SaveChangesAsync();
        return (item1, item2);
    }

    [Fact]
    public async Task CompleteGate_CountsApplicablePairs_NotFullSxI()
    {
        await using var db = TestHelpers.CreateContext(nameof(CompleteGate_CountsApplicablePairs_NotFullSxI));
        var fx = await SeedAsync(db);
        var (itemScoped, itemUniversal) = await ScopeItem1ToPos1Async(db, fx);
        var campaignId = await CreateCampaignAsync(db, fx);
        var controller = Ctx(db, true, null);

        // snapshots: AST-001@Pos1, AST-002@Pos2.
        var snap1 = (await db.MaintenanceCampaignDeviceSnapshots.Where(s => s.CampaignId == campaignId).OrderBy(s => s.AssetTag).ToListAsync())[0];
        var snap2 = (await db.MaintenanceCampaignDeviceSnapshots.Where(s => s.CampaignId == campaignId).OrderBy(s => s.AssetTag).ToListAsync())[1];

        // applicable pairs = itemScoped(1: Pos1) + itemUniversal(2: mọi snapshot) = 3 (KHÔNG phải 2×2=4).
        await controller.UpsertResult(campaignId, new UpsertCampaignResultDto(snap1.Id, itemScoped.Id, "OK", true, null));
        await controller.UpsertResult(campaignId, new UpsertCampaignResultDto(snap2.Id, itemUniversal.Id, "OK", true, null)); // 2/3

        var incomplete = await controller.Complete(campaignId);
        Assert.Equal("CAMPAIGN_RESULTS_INCOMPLETE", ErrorCode(incomplete));
        using (var doc = JsonDocument.Parse(JsonSerializer.Serialize((incomplete as BadRequestObjectResult)!.Value, WebJson)))
            Assert.Contains("2/3", doc.RootElement.GetProperty("message").GetString());

        await controller.UpsertResult(campaignId, new UpsertCampaignResultDto(snap1.Id, itemUniversal.Id, "OK", true, null)); // 3/3
        Assert.IsType<OkObjectResult>(await controller.Complete(campaignId));
        var c = await db.MaintenanceCampaigns.FindAsync(campaignId);
        Assert.Equal(MaintenanceCampaignStatus.Completed, c!.Status);
    }

    [Fact]
    public async Task UpsertResult_OutOfApplicablePosition_400_INVALID_ITEM_POSITION()
    {
        await using var db = TestHelpers.CreateContext(nameof(UpsertResult_OutOfApplicablePosition_400_INVALID_ITEM_POSITION));
        var fx = await SeedAsync(db);
        var (itemScoped, itemUniversal) = await ScopeItem1ToPos1Async(db, fx);
        var campaignId = await CreateCampaignAsync(db, fx);
        var controller = Ctx(db, true, null);

        var snaps = await db.MaintenanceCampaignDeviceSnapshots.Where(s => s.CampaignId == campaignId).OrderBy(s => s.AssetTag).ToListAsync();
        var snapPos1 = snaps[0]; // AST-001 @Pos1 (Pos1 order by tag = Pos1's asset)
        var snapPos2 = snaps[1]; // AST-002 @Pos2

        // Cặp ngoài phạm vi (itemScoped chỉ áp dụng Pos1, snapshot này ở Pos2) → 400.
        Assert.Equal("INVALID_ITEM_POSITION",
            ErrorCode(await controller.UpsertResult(campaignId, new UpsertCampaignResultDto(snapPos2.Id, itemScoped.Id, "X", true, null))));

        // Cặp trong phạm vi (Pos1) + item universal → OK.
        Assert.IsType<OkObjectResult>(await controller.UpsertResult(campaignId, new UpsertCampaignResultDto(snapPos1.Id, itemScoped.Id, "OK", true, null)));
        Assert.IsType<OkObjectResult>(await controller.UpsertResult(campaignId, new UpsertCampaignResultDto(snapPos2.Id, itemUniversal.Id, "OK", true, null)));
    }

    [Fact]
    public async Task StaleResultCleanup_InProgressOnly_CompletedImmutable()
    {
        await using var db = TestHelpers.CreateContext(nameof(StaleResultCleanup_InProgressOnly_CompletedImmutable));
        var fx = await SeedAsync(db);
        var (itemScoped, _) = await ScopeItem1ToPos1Async(db, fx);
        var campaignId = await CreateCampaignAsync(db, fx);
        var snaps = await db.MaintenanceCampaignDeviceSnapshots.Where(s => s.CampaignId == campaignId).OrderBy(s => s.AssetTag).ToListAsync();
        var snapPos1 = snaps[0];
        var snapPos2 = snaps[1];

        // Campaign InProgress: 1 kết quả hợp lệ + 1 kết quả THỪA (snapPos2 × itemScoped — chèn tay mô phỏng
        // dữ liệu sinh trước khi guard MC-7c).
        db.MaintenanceChecklistResults.Add(new MaintenanceChecklistResult { CampaignId = campaignId, DeviceSnapshotId = snapPos1.Id, ChecklistItemId = itemScoped.Id, IsPass = true });
        db.MaintenanceChecklistResults.Add(new MaintenanceChecklistResult { CampaignId = campaignId, DeviceSnapshotId = snapPos2.Id, ChecklistItemId = itemScoped.Id, IsPass = true });
        await db.SaveChangesAsync();

        // Campaign Completed (cùng version, status Completed) với cùng 1 kết quả thừa → BẤT BIẾN, không đụng.
        var completedId = Guid.NewGuid();
        db.MaintenanceCampaigns.Add(new MaintenanceCampaign
        {
            Id = completedId,
            SystemInfoId = fx.System.Id,
            TemplateVersionId = fx.Version.Id,
            StartDate = DateTime.UtcNow,
            Status = MaintenanceCampaignStatus.Completed,
            EndDate = DateTime.UtcNow,
            CompanyId = fx.Company.Id
        });
        db.MaintenanceChecklistResults.Add(new MaintenanceChecklistResult { CampaignId = completedId, DeviceSnapshotId = snapPos2.Id, ChecklistItemId = itemScoped.Id, IsPass = true });
        await db.SaveChangesAsync();

        // Replica cleanup (semantics đúng scripts/cleanup-maintenance-stale-results.sql; SQL thật verify ở E2E):
        // InProgress + item có khai báo vị trí + snapshot không nằm trong danh sách vị trí → xóa.
        var declaredPositions = await db.MaintenanceChecklistItemPositions
            .Where(ip => ip.ItemId == itemScoped.Id).Select(ip => ip.SystemPositionId).ToListAsync();
        var stale = await db.MaintenanceChecklistResults
            .Where(r =>
                r.Campaign.Status == MaintenanceCampaignStatus.InProgress &&
                r.ChecklistItemId == itemScoped.Id &&
                !declaredPositions.Contains(r.DeviceSnapshot.SystemPositionId ?? Guid.Empty))
            .ToListAsync();
        db.MaintenanceChecklistResults.RemoveRange(stale);
        await db.SaveChangesAsync();

        // InProgress: chỉ còn 1 kết quả (hợp lệ Pos1); Completed: vẫn giữ kết quả thừa (bất biến).
        Assert.Equal(1, await db.MaintenanceChecklistResults.CountAsync(r => r.CampaignId == campaignId));
        Assert.Equal(1, await db.MaintenanceChecklistResults.CountAsync(r => r.CampaignId == completedId));
    }
}