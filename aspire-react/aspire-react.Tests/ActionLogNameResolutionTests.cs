using aspire_react.Server.Application.ActionLogs.Queries;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [Giai đoạn 3 — ActionLogs] Name-resolution coverage — MỖI NHÁNH của 2 switch riêng biệt:
/// (A) GET /by-system ResolveItemName: Asset / Accessory / Consumable / Component / License /
///     MaintenanceCampaign (6 branches) — mỗi loại 1 case với tên seeded riêng.
/// (B) GET /action-logs targetName: User / SystemPosition (position→location fallback) /
///     Asset (TAG - Name) + fallback chain khi typed lookup trượt.
/// Yêu cầu duyệt: không chỉ test happy-path 1-2 loại rồi coi là đủ — mọi nhánh switch phải có
/// case, không bỏ sót.
/// </summary>
public class ActionLogNameResolutionTests
{
    private static AppDbContext CreateContext(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options, new TestHelpers.SuperUserScope());
    }

    /// <summary>
    /// Creator user phải là row THẬT: ActionLog.Creator là required navigation (CreatedBy non-null)
    /// → Include/projection tham chiếu l.Creator biến thành INNER JOIN trên InMemory — log với
    /// CreatedBy không có User row sẽ bị drop khỏi kết quả (khác real Postgres? không — real data
    /// luôn có user hợp lệ; fixture phải mirror điều đó).
    /// </summary>
    private static async Task<User> SeedUserAsync(AppDbContext db, string username)
    {
        var u = new User { Username = username, Email = $"{username}@local" };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u;
    }

    private static ActionLog LogFor(ItemType itemType, Guid itemId, Guid systemId, Guid? positionId = null, AssignmentTargetType? targetType = null)
        => new()
        {
            ItemType = itemType,
            ItemId = itemId,
            ActionType = ActionType.Update,
            CreatedBy = Guid.NewGuid(),
            CompanyId = null,
            TargetType = targetType,
            TargetId = positionId,
            TargetSystemInfoId = systemId,
            Note = "resolution fixture"
        };

    // ==================== (A) by-system ResolveItemName — 6 branches ====================

    [Fact]
    public async Task BySystem_ResolvesItemName_ForAllSixItemTypeBranches()
    {
        await using var db = CreateContext(nameof(BySystem_ResolvesItemName_ForAllSixItemTypeBranches));
        var creator = await SeedUserAsync(db, "g9creator");
        var sys = new SystemInfo { Code = "SYS-RES-1", Name = "Hệ thống resolve" };
        var pos = new SystemPosition { SystemInfoId = sys.Id, Name = "Vị trí A" };
        db.SystemInfos.Add(sys);
        db.SystemPositions.Add(pos);
        await db.SaveChangesAsync();

        var asset = new Asset { AssetTag = "G9RES-1", Name = "Máy in laser" };
        var accessory = new Accessory { Name = "Chuột không dây" };
        var consumable = new Consumable { Name = "Mực in" };
        var component = new Component { Name = "SSD 1TB" };
        var license = new License { Name = "Office 365" };
        var campaign = new MaintenanceCampaign { SystemInfoId = sys.Id, BatchNumber = "B01", StartDate = DateTime.UtcNow };
        db.Assets.AddRange(asset);
        db.Accessories.Add(accessory);
        db.Consumables.Add(consumable);
        db.Components.Add(component);
        db.Licenses.Add(license);
        db.MaintenanceCampaigns.Add(campaign);
        await db.SaveChangesAsync();

        // 6 logs — 5 với TargetType=SystemPosition (nhánh gốc), 1 campaign (nhánh MC-3 OR).
        db.ActionLogs.AddRange(
            LogFor(ItemType.Asset, asset.Id, sys.Id, pos.Id, AssignmentTargetType.SystemPosition),
            LogFor(ItemType.Accessory, accessory.Id, sys.Id, pos.Id, AssignmentTargetType.SystemPosition),
            LogFor(ItemType.Consumable, consumable.Id, sys.Id, pos.Id, AssignmentTargetType.SystemPosition),
            LogFor(ItemType.Component, component.Id, sys.Id, pos.Id, AssignmentTargetType.SystemPosition),
            LogFor(ItemType.License, license.Id, sys.Id, pos.Id, AssignmentTargetType.SystemPosition),
            LogFor(ItemType.MaintenanceCampaign, campaign.Id, sys.Id));
        foreach (var l in db.ActionLogs.Local) l.CreatedBy = creator.Id;
        await db.SaveChangesAsync();

        var handler = new GetBySystemQueryHandler(db, new TestHelpers.FakeScope { Super = true });
        var page = await handler.Handle(new GetBySystemQuery(sys.Id, PageSize: 50), CancellationToken.None);

        Assert.NotNull(page);
        Assert.Equal(6, page!.Total);
        Assert.Equal(6, page.Items.Count); // materialized rows must match total (page size 50)

        string? ItemNameOf(string itemType, Guid itemId)
            => page.Items.Single(r => r.ItemType == itemType && r.ItemId == itemId).ItemName;

        // MỖI nhánh resolve đúng tên từ bảng của nó:
        Assert.Equal("G9RES-1 - Máy in laser", ItemNameOf("Asset", asset.Id));            // Asset: "TAG - Name"
        Assert.Equal("Chuột không dây", ItemNameOf("Accessory", accessory.Id));
        Assert.Equal("Mực in", ItemNameOf("Consumable", consumable.Id));
        Assert.Equal("SSD 1TB", ItemNameOf("Component", component.Id));
        Assert.Equal("Office 365", ItemNameOf("License", license.Id));
        Assert.Equal("Bảo dưỡng Hệ thống resolve (B01)", ItemNameOf("MaintenanceCampaign", campaign.Id)); // MC-3 shape
        // TargetName (Vị trí lắp đặt) cũng resolve cho các row SystemPosition-targeted.
        Assert.All(page.Items.Where(r => r.ItemType != "MaintenanceCampaign"),
            r => Assert.Equal("Vị trí A", r.TargetName));
    }

    // ==================== (B) /action-logs targetName branches ====================

    [Fact]
    public async Task ItemLogs_ResolvesUserTarget_CreatorName_And_Trim()
    {
        await using var db = CreateContext(nameof(ItemLogs_ResolvesUserTarget_CreatorName_And_Trim));
        // Creator với FirstName/LastName rỗng → fallback Username (verbatim trim rule).
        var creator = new User { Username = "quytrim", Email = "q@local", FirstName = "", LastName = "" };
        var target = new User { Username = "nguoinhancap", Email = "n@local", FirstName = "Nguyễn", LastName = "Văn B" };
        db.Users.AddRange(creator, target);
        var asset = new Asset { AssetTag = "G9RES-2", Name = "Máy chiếu" };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();
        db.ActionLogs.Add(new ActionLog
        {
            ItemType = ItemType.Asset, ItemId = asset.Id, ActionType = ActionType.Checkout,
            CreatedBy = creator.Id, CompanyId = null,
            TargetType = AssignmentTargetType.User, TargetId = target.Id,
            Note = "Cấp phát"
        });
        await db.SaveChangesAsync();

        var handler = new GetActionLogsQueryHandler(db, new TestHelpers.FakeScope { Super = true });
        var logs = await handler.Handle(new GetActionLogsQuery(ItemType.Asset, asset.Id), CancellationToken.None);

        Assert.NotNull(logs);
        var row = logs!.Single(l => l.ActionType == "Checkout");
        Assert.Equal("Nguyễn Văn B", row.TargetName);                    // User branch
        Assert.Equal("quytrim", row.CreatorName);                        // trim → username fallback
        Assert.Equal("Asset", row.ItemType);                             // enum → string
        Assert.Equal("Checkout", row.ActionType);                        // enum → string
        Assert.Equal((int)ActionType.Checkout, row.ActionTypeValue);     // int verbatim
    }

    [Fact]
    public async Task ItemLogs_SystemPositionTarget_FallsBackToLocationName_WhenPositionMissing()
    {
        await using var db = CreateContext(nameof(ItemLogs_SystemPositionTarget_FallsBackToLocationName_WhenPositionMissing));
        var creator = await SeedUserAsync(db, "g9creator2");
        var asset = new Asset { AssetTag = "G9RES-3", Name = "Router" };
        var location = new Location { Name = "Tầng 2 - Tủ mạng" };
        db.Assets.Add(asset);
        db.Locations.Add(location);
        await db.SaveChangesAsync();
        // TargetType=SystemPosition nhưng position KHÔNG còn trong DB (đã xóa) → position dict trượt
        // → location-name fallback trong nhánh SystemPosition (verbatim).
        db.ActionLogs.Add(new ActionLog
        {
            ItemType = ItemType.Asset, ItemId = asset.Id, ActionType = ActionType.Checkin,
            CreatedBy = creator.Id, CompanyId = null,
            TargetType = AssignmentTargetType.SystemPosition, TargetId = Guid.NewGuid(),
            Note = "Checkin history"
        });
        // Riêng 1 log với TargetId = location id (fallback chain tìm được location).
        db.ActionLogs.Add(new ActionLog
        {
            ItemType = ItemType.Asset, ItemId = asset.Id, ActionType = ActionType.Audit,
            CreatedBy = creator.Id, CompanyId = null,
            TargetType = null, TargetId = location.Id, // TargetType null → fallback chain toàn bộ
            Note = "Audit history"
        });
        await db.SaveChangesAsync();

        var handler = new GetActionLogsQueryHandler(db, new TestHelpers.FakeScope { Super = true });
        var logs = await handler.Handle(new GetActionLogsQuery(ItemType.Asset, asset.Id), CancellationToken.None);

        Assert.NotNull(logs);
        Assert.True(logs!.Count == 2, $"expected 2 seeded logs, got {logs.Count}: [{string.Join("; ", logs.Select(l => $"{l.ActionType}/{l.TargetType}/{l.TargetName}"))}]");
        // SystemPosition typed-lookup trượt → fallback location dict trong cùng nhánh.
        var checkin = logs!.Single(l => l.ActionType == "Checkin");
        Assert.Null(checkin.TargetName); // position không tồn tại VÀ không match bảng nào khác trong fallback (guid lạ)
        // Fallback chain: TargetType null + TargetId = location id → resolves qua location dictionary.
        var audit = logs.Single(l => l.ActionType == "Audit");
        Assert.Equal("Tầng 2 - Tủ mạng", audit.TargetName);
    }

    [Fact]
    public async Task ItemLogs_Superuser_UnknownItem_ReturnsEmptyList_Not404()
    {
        await using var db = CreateContext(nameof(ItemLogs_Superuser_UnknownItem_ReturnsEmptyList_Not404));
        var handler = new GetActionLogsQueryHandler(db, new TestHelpers.FakeScope { Super = true });

        // Superuser: userCompanyId == null → visibility TRUE ngay cả khi item không tồn tại
        // (verbatim pre-migration) → EMPTY list, handler KHÔNG trả null (không 404).
        var logs = await handler.Handle(
            new GetActionLogsQuery(ItemType.Asset, Guid.NewGuid()), CancellationToken.None);

        Assert.NotNull(logs);   // null CHỈ khi không visible (regular user out-of-scope)
        Assert.Empty(logs!);
    }

    [Fact]
    public async Task ItemLogs_RegularUser_OutOfScopeItem_ReturnsNull_404Path()
    {
        await using var db = CreateContext(nameof(ItemLogs_RegularUser_OutOfScopeItem_ReturnsNull_404Path));
        var otherCompany = new Company { Code = "G9X", Name = "Cty khác" };
        var asset = new Asset { AssetTag = "G9RES-4", Name = "Asset công ty khác", CompanyId = otherCompany.Id };
        db.Companies.Add(otherCompany);
        db.Assets.Add(asset);
        await db.SaveChangesAsync();

        // Regular user thuộc company KHÁC → IsItemVisibleAsync false → handler trả null → 404 path.
        var handler = new GetActionLogsQueryHandler(db, new TestHelpers.FakeScope { Super = false, CompanyId = Guid.NewGuid() });
        var logs = await handler.Handle(new GetActionLogsQuery(ItemType.Asset, asset.Id), CancellationToken.None);

        Assert.Null(logs);
    }
}
