using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// MC-2 — MaintenanceTemplatesController: template CRUD company-scoping (Q1), draft→publish
/// lifecycle (IsCurrent flip), and the TEMPLATE_VERSION_IN_USE immutable guard once a campaign
/// pins a version (verified at handler level; DB-level RESTRICT FK is verified live on Postgres).
/// </summary>
public class MaintenanceTemplateTests
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

    /// <summary>Seed 2 companies + 1 system each; returns ids.</summary>
    private static async Task<(Guid companyA, Guid companyB, Guid sysA, Guid sysB)> SeedCompaniesAsync(AppDbContext ctx)
    {
        var coA = new Company { Code = "COA", Name = "Công ty A" };
        var coB = new Company { Code = "COB", Name = "Công ty B" };
        var sysA = new SystemInfo { Code = "SYS-2026-001", Name = "Hệ thống A", CompanyId = coA.Id };
        var sysB = new SystemInfo { Code = "SYS-2026-002", Name = "Hệ thống B", CompanyId = coB.Id };
        ctx.Companies.AddRange(coA, coB);
        ctx.SystemInfos.AddRange(sysA, sysB);
        await ctx.SaveChangesAsync();
        return (coA.Id, coB.Id, sysA.Id, sysB.Id);
    }

    private static MaintenanceTemplatesController Ctx(AppDbContext db, bool super, Guid? companyId)
        => new(TestHelpers.BuildMediator(db, new TestHelpers.FakeScope { Super = super, CompanyId = companyId }),
            db, new TestHelpers.FakeCurrentUser(), new TestHelpers.FakeScope { Super = super, CompanyId = companyId },
            TestHelpers.CreateActionLogService(db));

    /// <summary>Create template as superuser through the API and return (template, draft version).</summary>
    private static async Task<(MaintenanceChecklistTemplate t, MaintenanceChecklistTemplateVersion v)>
        CreateTemplateAsync(AppDbContext db, Guid sysId, Guid? companyId, string name = "Template bảo dưỡng A")
    {
        var controller = Ctx(db, super: true, companyId: null);
        var result = await controller.Create(new CreateMaintenanceTemplateDto(name, sysId, companyId));
        var ok = Assert.IsType<OkObjectResult>(result);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var data = doc.RootElement.GetProperty("data");
        var t = await db.MaintenanceChecklistTemplates.Include(x => x.Versions).SingleAsync(x => x.Name == name);
        var v = t.Versions.Single();
        Assert.Equal(t.Id, doc.RootElement.GetProperty("data").GetProperty("id").GetGuid());
        Assert.Equal(v.Id, data.GetProperty("initialVersionId").GetGuid());
        return (t, v);
    }

    // ==================== Create + company scoping ====================

    [Fact]
    public async Task Create_MakesDraftVersion1_NotPublished_NotCurrent()
    {
        await using var db = TestHelpers.CreateContext(nameof(Create_MakesDraftVersion1_NotPublished_NotCurrent));
        var (_, _, sysA, _) = await SeedCompaniesAsync(db);

        var (_, v) = await CreateTemplateAsync(db, sysA, companyId: null);

        Assert.Equal(1, v.VersionNumber);
        Assert.Null(v.PublishedAt);
        Assert.False(v.IsCurrent);
        Assert.True(await db.ActionLogs.AnyAsync(l =>
            l.ItemType == ItemType.MaintenanceChecklistTemplate && l.ActionType == ActionType.Create));
    }

    [Fact]
    public async Task Create_OtherCompany_CompanyMismatch()
    {
        await using var db = TestHelpers.CreateContext(nameof(Create_OtherCompany_CompanyMismatch));
        var (coA, coB, sysA, _) = await SeedCompaniesAsync(db);
        var controller = Ctx(db, super: false, companyId: coA);

        var result = await controller.Create(new CreateMaintenanceTemplateDto("T1", sysA, coB));

        Assert.Equal("COMPANY_MISMATCH", ErrorCode(result));
    }

    [Fact]
    public async Task Create_SystemOutOfScope_404HidesExistence()
    {
        await using var db = TestHelpers.CreateContext(nameof(Create_SystemOutOfScope_404HidesExistence));
        var (coA, _, _, sysB) = await SeedCompaniesAsync(db);
        var controller = Ctx(db, super: false, companyId: coA);

        var result = await controller.Create(new CreateMaintenanceTemplateDto("T1", sysB, null));

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetAll_UserSeesOwnAndFloater_NeverOtherCompany()
    {
        await using var db = TestHelpers.CreateContext(nameof(GetAll_UserSeesOwnAndFloater_NeverOtherCompany));
        var (coA, coB, sysA, sysB) = await SeedCompaniesAsync(db);
        await CreateTemplateAsync(db, sysA, coA, name: "A-own");
        await CreateTemplateAsync(db, sysB, coB, name: "B-other");
        await CreateTemplateAsync(db, sysB, null, name: "floater");

        var controller = Ctx(db, super: false, companyId: coA);
        var ok = Assert.IsType<OkObjectResult>(await controller.GetAll(null));
        var names = TestHelpers.ReadDataStringArray(ok.Value, "name");

        Assert.Contains("A-own", names);
        Assert.Contains("floater", names);   // floater visible cross-company
        Assert.DoesNotContain("B-other", names);

        var superOk = Assert.IsType<OkObjectResult>(await Ctx(db, true, null).GetAll(null));
        Assert.Equal(3, TestHelpers.ReadDataCount(superOk.Value));
    }

    [Fact]
    public async Task UpdateAndDelete_OutOfScope_404HidesExistence()
    {
        await using var db = TestHelpers.CreateContext(nameof(UpdateAndDelete_OutOfScope_404HidesExistence));
        var (coA, coB, _, sysB) = await SeedCompaniesAsync(db);
        var (t, _) = await CreateTemplateAsync(db, sysB, coB, name: "B-only");

        var userController = Ctx(db, super: false, companyId: coA);
        Assert.IsType<NotFoundObjectResult>(await userController.Update(t.Id, new UpdateMaintenanceTemplateDto(null, null, null, null)));
        Assert.IsType<NotFoundObjectResult>(await userController.Delete(t.Id));
        Assert.Equal(1, await db.MaintenanceChecklistTemplates.CountAsync(x => x.Id == t.Id));
    }

    // ==================== Publish lifecycle ====================

    [Fact]
    public async Task Publish_SetsCurrent_AndDemotesOldVersion()
    {
        await using var db = TestHelpers.CreateContext(nameof(Publish_SetsCurrent_AndDemotesOldVersion));
        var (_, _, sysA, _) = await SeedCompaniesAsync(db);
        var controller = Ctx(db, true, null);
        var (t, v1) = await CreateTemplateAsync(db, sysA, null, name: "Pub");

        // v1 publish → current
        Assert.IsType<OkObjectResult>(await controller.PublishVersion(t.Id, v1.Id));
        // v2 draft + publish → must demote v1
        var created = Assert.IsType<OkObjectResult>(await controller.CreateVersion(t.Id, new CreateTemplateVersionDto(DateTime.UtcNow)));
        Guid v2Id;
        using (var doc = JsonDocument.Parse(JsonSerializer.Serialize(created.Value, WebJson)))
            v2Id = doc.RootElement.GetProperty("data").GetProperty("id").GetGuid();

        Assert.IsType<OkObjectResult>(await controller.PublishVersion(t.Id, v2Id));

        var versions = await db.MaintenanceChecklistTemplateVersions.AsNoTracking()
            .Where(v => v.TemplateId == t.Id).OrderBy(v => v.VersionNumber).ToListAsync();
        Assert.False(versions[0].IsCurrent, "old version must be demoted");
        Assert.NotNull(versions[0].PublishedAt);
        Assert.True(versions[1].IsCurrent);
        Assert.NotNull(versions[1].PublishedAt);
        Assert.Equal(2, versions.Count); // nothing deleted
    }

    [Fact]
    public async Task Publish_AlreadyPublished_400()
    {
        await using var db = TestHelpers.CreateContext(nameof(Publish_AlreadyPublished_400));
        var (_, _, sysA, _) = await SeedCompaniesAsync(db);
        var controller = Ctx(db, true, null);
        var (t, v1) = await CreateTemplateAsync(db, sysA, null, name: "Repub");

        Assert.IsType<OkObjectResult>(await controller.PublishVersion(t.Id, v1.Id));
        var second = await controller.PublishVersion(t.Id, v1.Id);

        Assert.Equal("VERSION_ALREADY_PUBLISHED", ErrorCode(second));
    }

    [Fact]
    public async Task DeleteVersion_PublishedBlocked_DraftAllowed()
    {
        await using var db = TestHelpers.CreateContext(nameof(DeleteVersion_PublishedBlocked_DraftAllowed));
        var (_, _, sysA, _) = await SeedCompaniesAsync(db);
        var controller = Ctx(db, true, null);
        var (t, v1) = await CreateTemplateAsync(db, sysA, null, name: "DelV");
        Assert.IsType<OkObjectResult>(await controller.PublishVersion(t.Id, v1.Id));
        var v2Created = Assert.IsType<OkObjectResult>(await controller.CreateVersion(t.Id, null));
        Guid v2Id;
        using (var doc = JsonDocument.Parse(JsonSerializer.Serialize(v2Created.Value, WebJson)))
            v2Id = doc.RootElement.GetProperty("data").GetProperty("id").GetGuid();

        Assert.Equal("VERSION_ALREADY_PUBLISHED", ErrorCode(await controller.DeleteVersion(t.Id, v1.Id)));
        Assert.IsType<OkObjectResult>(await controller.DeleteVersion(t.Id, v2Id));
        Assert.Equal(1, await db.MaintenanceChecklistTemplateVersions.CountAsync(v => v.TemplateId == t.Id));
    }

    // ==================== Immutable guard (campaign pins version) ====================

    /// <summary>Simulate MC-3: a campaign directly references the version (the same state the real
    /// campaign-create endpoint will produce; E2E verifies this against live Postgres too).</summary>
    private static async Task<MaintenanceCampaign> PinCampaignAsync(AppDbContext db, MaintenanceChecklistTemplate t,
        MaintenanceChecklistTemplateVersion v, Guid sysId, Guid? companyId)
    {
        var campaign = new MaintenanceCampaign
        {
            SystemInfoId = sysId,
            TemplateVersionId = v.Id,
            StartDate = DateTime.UtcNow,
            BatchNumber = "DOT-01",
            CompanyId = companyId
        };
        db.MaintenanceCampaigns.Add(campaign);
        await db.SaveChangesAsync();
        return campaign;
    }

    [Fact]
    public async Task VersionWithCampaign_EditDelete_BlockedWithTemplateVersionInUse()
    {
        await using var db = TestHelpers.CreateContext(nameof(VersionWithCampaign_EditDelete_BlockedWithTemplateVersionInUse));
        var (coA, _, sysA, _) = await SeedCompaniesAsync(db);
        var controller = Ctx(db, true, null);
        var (t, v1) = await CreateTemplateAsync(db, sysA, coA, name: "Frozen");
        await controller.PublishVersion(t.Id, v1.Id);
        await PinCampaignAsync(db, t, v1, sysA, coA);

        Assert.Equal("TEMPLATE_VERSION_IN_USE", ErrorCode(await controller.UpdateVersion(t.Id, v1.Id, new UpdateTemplateVersionDto(DateTime.UtcNow))));
        Assert.Equal("TEMPLATE_VERSION_IN_USE", ErrorCode(await controller.DeleteVersion(t.Id, v1.Id)));
        Assert.Equal("TEMPLATE_VERSION_IN_USE",
            ErrorCode(await controller.AddItem(t.Id, v1.Id, new MaintenanceChecklistItemDto(null, "Vệ sinh quạt", null, null, null))));
        // [MC-8] AddParam giờ nhận context của 1 ChecklistItem — tạo item trực tiếp trong DB rồi gọi AddParam.
        var frozenItem = new MaintenanceChecklistItem { TemplateVersionId = v1.Id, Order = 1, Name = "Vệ sinh quạt", CycleMonths = 12 };
        db.MaintenanceChecklistItems.Add(frozenItem);
        await db.SaveChangesAsync();
        Assert.Equal("TEMPLATE_VERSION_IN_USE",
            ErrorCode(await controller.AddParam(t.Id, v1.Id, frozenItem.Id, new MaintenanceStandardParamDto("CPU load", "<70%", null, null, null))));
    }

    [Fact]
    public async Task VersionWithoutCampaign_ItemsEditable_EvenAfterPublish()
    {
        await using var db = TestHelpers.CreateContext(nameof(VersionWithoutCampaign_ItemsEditable_EvenAfterPublish));
        var (_, _, sysA, _) = await SeedCompaniesAsync(db);
        var controller = Ctx(db, true, null);
        var (t, v1) = await CreateTemplateAsync(db, sysA, null, name: "EditWindow");
        await controller.PublishVersion(t.Id, v1.Id); // published but ZERO campaigns

        var added = Assert.IsType<OkObjectResult>(
            await controller.AddItem(t.Id, v1.Id, new MaintenanceChecklistItemDto(null, "Kiểm tra ổ đĩa", 6, null, null)));
        Guid itemId;
        using (var doc = JsonDocument.Parse(JsonSerializer.Serialize(added.Value, WebJson)))
        {
            itemId = doc.RootElement.GetProperty("data").GetProperty("id").GetGuid();
            Assert.Equal(1, doc.RootElement.GetProperty("data").GetProperty("order").GetInt32()); // auto order
        }

        // duplicate explicit order rejected cleanly (no raw unique violation)
        Assert.Equal("ITEM_ORDER_TAKEN",
            ErrorCode(await controller.AddItem(t.Id, v1.Id, new MaintenanceChecklistItemDto(1, "Trùng thứ tự", null, null, null))));

        // patch semantics: absent fields untouched
        Assert.IsType<OkObjectResult>(
            await controller.UpdateItem(t.Id, v1.Id, itemId, new MaintenanceChecklistItemDto(null, "Kiểm tra ổ đĩa + RAID", 6, null, null)));
        var item = await db.MaintenanceChecklistItems.SingleAsync(i => i.Id == itemId);
        Assert.Equal("Kiểm tra ổ đĩa + RAID", item.Name);
        Assert.Equal(6, item.CycleMonths);
    }

    [Fact]
    public async Task DeleteTemplate_WithCampaignOnAnyVersion_Blocked()
    {
        await using var db = TestHelpers.CreateContext(nameof(DeleteTemplate_WithCampaignOnAnyVersion_Blocked));
        var (coA, _, sysA, _) = await SeedCompaniesAsync(db);
        var controller = Ctx(db, true, null);
        var (t, v1) = await CreateTemplateAsync(db, sysA, coA, name: "Guarded");
        await PinCampaignAsync(db, t, v1, sysA, coA);

        Assert.Equal("TEMPLATE_IN_USE", ErrorCode(await controller.Delete(t.Id)));
        Assert.Equal(1, await db.MaintenanceChecklistTemplates.CountAsync(x => x.Id == t.Id));
    }

    [Fact]
    public async Task UpdateTemplate_SystemChange_LockedOnlyWhenCampaignExists()
    {
        await using var db = TestHelpers.CreateContext(nameof(UpdateTemplate_SystemChange_LockedOnlyWhenCampaignExists));
        var (coA, _, sysA, _) = await SeedCompaniesAsync(db);
        // Second system INSIDE company A so the free template's move stays in scope.
        var sysA2 = new SystemInfo { Code = "SYS-2026-003", Name = "Hệ thống A2", CompanyId = coA };
        db.SystemInfos.Add(sysA2);
        await db.SaveChangesAsync();

        var controller = Ctx(db, false, coA); // regular user of company A owns sysA templates
        var (tFree, _) = await CreateTemplateAsync(db, sysA, coA, name: "Movable");
        var (tLocked, vLocked) = await CreateTemplateAsync(db, sysA, coA, name: "Pinned");
        await PinCampaignAsync(db, tLocked, vLocked, sysA, coA);

        // No campaigns yet → move allowed.
        Assert.IsType<OkObjectResult>(await controller.Update(tFree.Id, new UpdateMaintenanceTemplateDto(null, sysA2.Id, null, null)));

        // Pinned by campaign → FIELD_LOCKED.
        Assert.Equal("FIELD_LOCKED",
            ErrorCode(await controller.Update(tLocked.Id, new UpdateMaintenanceTemplateDto(null, sysA2.Id, null, null))));
    }

    // ==================== MC-7b: positionIds (phạm vi vị trí áp dụng) ====================

    [Fact]
    public async Task AddItem_WithPositionIds_PersistsAndReturnsNamesInVersionDetail()
    {
        await using var db = TestHelpers.CreateContext(nameof(AddItem_WithPositionIds_PersistsAndReturnsNamesInVersionDetail));
        var (coA, _, sysA, _) = await SeedCompaniesAsync(db);
        var pos1 = new SystemPosition { Code = "P1-2026-001", Name = "Vị trí SDP1", SystemInfoId = sysA };
        db.SystemPositions.Add(pos1);
        await db.SaveChangesAsync();

        var controller = Ctx(db, true, null);
        var (t, v1) = await CreateTemplateAsync(db, sysA, coA, name: "PosItem");

        var created = Assert.IsType<OkObjectResult>(await controller.AddItem(t.Id, v1.Id,
            new MaintenanceChecklistItemDto(null, "Hạng mục SDP", 6, null, null, new[] { pos1.Id })));

        using (var doc = JsonDocument.Parse(JsonSerializer.Serialize(created.Value, WebJson)))
        {
            var ids = doc.RootElement.GetProperty("data").GetProperty("positionIds");
            Assert.Equal(1, ids.GetArrayLength());
            Assert.Equal(pos1.Id, ids[0].GetGuid());
        }

        // GET version trả kèm positionIds + names.
        var detail = Assert.IsType<OkObjectResult>(await controller.GetVersion(t.Id, v1.Id));
        using var d = JsonDocument.Parse(JsonSerializer.Serialize(detail.Value, WebJson));
        var item = d.RootElement.GetProperty("data").GetProperty("items")[0];
        Assert.Equal("Vị trí SDP1", item.GetProperty("positionNames")[0].GetString());
        Assert.True(db.MaintenanceChecklistItemPositions.Any(ip => ip.ItemId == item.GetProperty("id").GetGuid() && ip.SystemPositionId == pos1.Id));
    }

    [Fact]
    public async Task AddItem_PositionOfAnotherSystem_InvalidPosition_400()
    {
        await using var db = TestHelpers.CreateContext(nameof(AddItem_PositionOfAnotherSystem_InvalidPosition_400));
        var (coA, _, sysA, sysB) = await SeedCompaniesAsync(db);
        var foreignPos = new SystemPosition { Code = "P2-2026-001", Name = "Vị trí hệ thống B", SystemInfoId = sysB };
        db.SystemPositions.Add(foreignPos);
        await db.SaveChangesAsync();

        var controller = Ctx(db, true, null);
        var (t, v1) = await CreateTemplateAsync(db, sysA, coA, name: "PosInvalid");

        var result = await controller.AddItem(t.Id, v1.Id,
            new MaintenanceChecklistItemDto(null, "Sai hệ thống", 6, null, null, new[] { foreignPos.Id }));

        Assert.Equal("INVALID_POSITION", ErrorCode(result));
        Assert.Equal(0, await db.MaintenanceChecklistItemPositions.CountAsync());
    }

    [Fact]
    public async Task UpdateItem_ReplacePositions_EmptyIsUniversal_AbsentKeeps()
    {
        await using var db = TestHelpers.CreateContext(nameof(UpdateItem_ReplacePositions_EmptyIsUniversal_AbsentKeeps));
        var (coA, _, sysA, _) = await SeedCompaniesAsync(db);
        var pos1 = new SystemPosition { Code = "P3-2026-001", Name = "Vị trí 1", SystemInfoId = sysA };
        var pos2 = new SystemPosition { Code = "P3-2026-002", Name = "Vị trí 2", SystemInfoId = sysA };
        db.SystemPositions.AddRange(pos1, pos2);
        await db.SaveChangesAsync();

        var controller = Ctx(db, true, null);
        var (t, v1) = await CreateTemplateAsync(db, sysA, coA, name: "PosUpdate");

        var created = Assert.IsType<OkObjectResult>(await controller.AddItem(t.Id, v1.Id,
            new MaintenanceChecklistItemDto(null, "Item", 6, null, null, new[] { pos1.Id })));
        Guid itemId;
        using (var doc = JsonDocument.Parse(JsonSerializer.Serialize(created.Value, WebJson)))
            itemId = doc.RootElement.GetProperty("data").GetProperty("id").GetGuid();

        // Gửi [pos2] → thay toàn bộ (pos1 bị thay, không gộp).
        Assert.IsType<OkObjectResult>(await controller.UpdateItem(t.Id, v1.Id, itemId,
            new MaintenanceChecklistItemDto(null, "Item", null, null, null, new[] { pos2.Id })));
        var positions = await db.MaintenanceChecklistItemPositions.Where(ip => ip.ItemId == itemId).ToListAsync();
        Assert.Single(positions);
        Assert.Equal(pos2.Id, positions[0].SystemPositionId);

        // Gửi [] → universal (0 dòng).
        Assert.IsType<OkObjectResult>(await controller.UpdateItem(t.Id, v1.Id, itemId,
            new MaintenanceChecklistItemDto(null, "Item", null, null, null, Array.Empty<Guid>())));
        Assert.Equal(0, await db.MaintenanceChecklistItemPositions.CountAsync(ip => ip.ItemId == itemId));

        // KHÔNG gửi PositionIds (null) → không đụng (patch semantics).
        Assert.IsType<OkObjectResult>(await controller.UpdateItem(t.Id, v1.Id, itemId,
            new MaintenanceChecklistItemDto(null, "Item", null, null, null)));
        Assert.Equal(0, await db.MaintenanceChecklistItemPositions.CountAsync(ip => ip.ItemId == itemId));
    }
}
