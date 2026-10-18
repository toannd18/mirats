using System.Text.Json;
using aspire_react.Server.Application.Assets.Queries;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [LL-3] Lý lịch thiết bị (<c>GET /api/v1/assets/{id}/timeline</c>) — trộn 4 nguồn, khử trùng
/// checkout/checkin tới vị trí, bỏ dòng đã đính chính, phân trang CURSOR (không offset).
/// <para>
/// Chạy trên InMemory (nhanh, logic thuần) — riêng hành vi SQL thật (uuid comparison, index) được
/// kiểm chứng trên Postgres thật ở bước evidence của LL-3.
/// </para>
/// </summary>
public class AssetTimelineQueryTests
{
    private static readonly DateTime Base = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);

    private sealed class Fixture
    {
        public required Asset Asset { get; init; }
        public required User Creator { get; init; }
        public required SystemInfo System { get; init; }
        public required MaintenanceCampaign Campaign { get; init; }
    }

    private static AssetTimelineQueryHandler Handler(AppDbContext db, ICompanyScopeService? scope = null)
        => new(db, scope ?? new TestHelpers.SuperUserScope());

    private static async Task<Fixture> SeedAsync(AppDbContext db, Guid? assetCompanyId = null, DateTime? campaignStart = null)
    {
        var creator = new User { Username = "qa-ll3", FirstName = "Kiểm", LastName = "Thử" };
        var asset = new Asset
        {
            AssetTag = "LL3-AST",
            Name = "Thiết bị LL-3",
            IsConfirmed = true,
            CompanyId = assetCompanyId
        };
        var system = new SystemInfo { Code = "LL3-SYS", Name = "Hệ thống LL-3" };
        var campaign = new MaintenanceCampaign
        {
            SystemInfoId = system.Id,
            TemplateVersionId = Guid.NewGuid(),
            StartDate = campaignStart ?? Base.AddDays(3),
            BatchNumber = "DOT-2026-03",
            Status = MaintenanceCampaignStatus.Completed,
            ReviewerId = creator.Id
        };

        db.Users.Add(creator);
        db.Assets.Add(asset);
        db.SystemInfos.Add(system);
        db.MaintenanceCampaigns.Add(campaign);
        await db.SaveChangesAsync();

        return new Fixture { Asset = asset, Creator = creator, System = system, Campaign = campaign };
    }

    private static AssetInstallation LedgerRow(Fixture fx, Guid? positionId, string? positionName, DateTime startedAt,
        DateTime? endedAt = null, AssetInstallationSource source = AssetInstallationSource.Api,
        string? loggedByName = "Người Lắp", DateTime? supersededAt = null)
        => new()
        {
            AssetId = fx.Asset.Id,
            SystemPositionId = positionId,
            SystemPositionName = positionName,
            SystemInfoId = fx.System.Id,
            SystemInfoName = fx.System.Name,
            StartedAt = startedAt,
            EndedAt = endedAt,
            SupersededAt = supersededAt,
            LoggedById = loggedByName is null ? null : fx.Creator.Id,
            LoggedByName = loggedByName,
            Source = source,
            Note = "ghi chú lý lịch"
        };

    // ==================== Trộn 4 nguồn + thứ tự ====================

    [Fact]
    public async Task Timeline_Merges_FourSources_InDescendingOrder()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_Merges_FourSources_InDescendingOrder));
        var fx = await SeedAsync(db);
        var position = new SystemPosition { Code = "LL3-POS", Name = "Vị trí 1", SystemInfoId = fx.System.Id };
        db.SystemPositions.Add(position);

        // Nguồn 1: lý lịch lắp đặt — 1 khoảng ĐÃ ĐÓNG ⇒ 2 sự kiện (lắp + tháo).
        db.AssetInstallations.Add(LedgerRow(fx, position.Id, position.Name, Base.AddDays(1), Base.AddDays(5)));

        // Nguồn 2: ActionLog — checkout cho user + kiểm kê (đều KHÔNG target vị trí ⇒ giữ).
        db.ActionLogs.Add(new ActionLog
        {
            ItemType = ItemType.Asset,
            ItemId = fx.Asset.Id,
            ActionType = ActionType.Checkout,
            TargetType = AssignmentTargetType.User,
            TargetId = fx.Creator.Id,
            CreatedBy = fx.Creator.Id,
            ActionDate = Base.AddDays(2)
        });
        db.ActionLogs.Add(new ActionLog
        {
            ItemType = ItemType.Asset,
            ItemId = fx.Asset.Id,
            ActionType = ActionType.Audit,
            CreatedBy = fx.Creator.Id,
            ActionDate = Base.AddDays(6),
            Note = "kiểm kê định kỳ"
        });

        // Nguồn 3: đợt bảo dưỡng (qua DeviceSnapshot chứa asset).
        db.MaintenanceCampaignDeviceSnapshots.Add(new MaintenanceCampaignDeviceSnapshot
        {
            CampaignId = fx.Campaign.Id,
            AssetId = fx.Asset.Id,
            AssetTag = fx.Asset.AssetTag,
            AssetName = fx.Asset.Name,
            SystemPositionName = position.Name,
            CreatedAt = Base.AddDays(3)
        });

        // Nguồn 4: phiếu bảo dưỡng.
        db.AssetMaintenances.Add(new AssetMaintenance
        {
            AssetId = fx.Asset.Id,
            Title = "Thay quạt",
            Type = AssetMaintenanceType.Repair,
            CompanyId = Guid.NewGuid(),
            StartDate = Base.AddDays(4),
            CreatedById = fx.Creator.Id
        });

        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50), CancellationToken.None);

        Assert.True(result.Success);
        var events = result.Events!;

        // MỚI NHẤT TRƯỚC, trộn đúng 4 nguồn.
        Assert.Equal(
            new[] { "AUDIT", "REMOVE", "MAINTENANCE", "MAINTENANCE_CAMPAIGN", "CHECKOUT", "INSTALL" },
            events.Select(e => e.EventType).ToArray());

        Assert.Equal(AssetTimelineRefTypes.ActionLog, events[0].RefType);
        Assert.Equal(AssetTimelineRefTypes.AssetInstallation, events[1].RefType);
        Assert.Equal(AssetTimelineRefTypes.AssetMaintenance, events[2].RefType);
        Assert.Equal(AssetTimelineRefTypes.MaintenanceCampaign, events[3].RefType);
        Assert.Equal(AssetTimelineRefTypes.ActionLog, events[4].RefType);
        Assert.Equal(AssetTimelineRefTypes.AssetInstallation, events[5].RefType);

        // refId trỏ đúng bản ghi nguồn (FE mở link).
        Assert.All(events, e => Assert.NotNull(e.RefId));

        // Nội dung tiếng Việt + người thực hiện.
        Assert.Equal("Lắp đặt tại: Vị trí 1", events[5].Title);
        Assert.Contains("Hệ thống: Hệ thống LL-3", events[5].Details);
        Assert.Equal("Người Lắp", events[5].ActorName);          // tên denormalize của lý lịch
        Assert.Equal("Tháo khỏi: Vị trí 1", events[1].Title);

        Assert.Equal("Cấp phát", events[4].Title);
        Assert.Contains("cho Kiểm Thử", events[4].Details);       // tra cứu tên user theo lô
        Assert.Equal("Kiểm Thử", events[4].ActorName);

        Assert.Contains("Thay quạt", events[2].Title);
        Assert.Contains("Sửa chữa", events[2].Title);
        Assert.Equal("Kiểm Thử", events[2].ActorName);

        Assert.Contains("DOT-2026-03", events[3].Title);
        Assert.Equal("Kiểm Thử", events[3].ActorName);           // người phụ trách đợt

        // Không nguồn nào bị đánh dấu ước tính (không có dòng backfill).
        Assert.All(events, e => Assert.False(e.IsEstimated));
        Assert.Null(result.NextCursor);
    }

    // ==================== Khử trùng theo điều kiện lọc đã nêu ====================

    [Fact]
    public async Task Timeline_CheckoutCheckin_ToSystemPosition_ComeFromLedgerOnly()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_CheckoutCheckin_ToSystemPosition_ComeFromLedgerOnly));
        var fx = await SeedAsync(db);
        var position = new SystemPosition { Code = "LL3-POS", Name = "Vị trí 9", SystemInfoId = fx.System.Id };
        db.SystemPositions.Add(position);

        // Lý lịch: lắp rồi tháo.
        db.AssetInstallations.Add(LedgerRow(fx, position.Id, position.Name, Base.AddDays(1), Base.AddDays(3)));

        // ActionLog của ĐÚNG thao tác đó (target = vị trí) ⇒ PHẢI bị bỏ (nếu không sẽ lặp).
        db.ActionLogs.Add(new ActionLog
        {
            ItemType = ItemType.Asset,
            ItemId = fx.Asset.Id,
            ActionType = ActionType.Checkout,
            TargetType = AssignmentTargetType.SystemPosition,
            TargetId = position.Id,
            CreatedBy = fx.Creator.Id,
            ActionDate = Base.AddDays(1)
        });
        db.ActionLogs.Add(new ActionLog
        {
            ItemType = ItemType.Asset,
            ItemId = fx.Asset.Id,
            ActionType = ActionType.Checkin,
            TargetType = AssignmentTargetType.SystemPosition,
            TargetId = position.Id,
            CreatedBy = fx.Creator.Id,
            ActionDate = Base.AddDays(3)
        });

        // Checkin KHÔNG target vị trí (VD thu hồi từ user) ⇒ PHẢI giữ.
        db.ActionLogs.Add(new ActionLog
        {
            ItemType = ItemType.Asset,
            ItemId = fx.Asset.Id,
            ActionType = ActionType.Checkin,
            TargetType = AssignmentTargetType.User,
            TargetId = fx.Creator.Id,
            CreatedBy = fx.Creator.Id,
            ActionDate = Base.AddDays(4)
        });

        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50), CancellationToken.None);

        Assert.True(result.Success);
        var events = result.Events!;

        // Mỗi lần gắn/tháo chỉ xuất hiện ĐÚNG MỘT lần (từ lý lịch), cộng 1 checkin từ user.
        Assert.Equal(new[] { "CHECKIN", "REMOVE", "INSTALL" }, events.Select(e => e.EventType).ToArray());
        Assert.Equal(2, events.Count(e => e.RefType == AssetTimelineRefTypes.AssetInstallation));
        Assert.Single(events, e => e.EventType == "CHECKIN");
    }

    [Fact]
    public async Task Timeline_Assignment_IsNotASource()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_Assignment_IsNotASource));
        var fx = await SeedAsync(db);

        db.Assignments.Add(new Assignment
        {
            AssetId = fx.Asset.Id,
            TargetType = AssignmentTargetType.User,
            TargetId = fx.Creator.Id,
            AssignedById = fx.Creator.Id,
            AssignedAt = Base.AddDays(2)
        });
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50), CancellationToken.None);

        Assert.True(result.Success);
        // Assignment KHÔNG sinh sự kiện (ActionLog mới là nguồn) ⇒ timeline rỗng.
        Assert.Empty(result.Events!);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task Timeline_Excludes_SupersededLedgerRow_And_SoftDeletedRecords()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_Excludes_SupersededLedgerRow_And_SoftDeletedRecords));
        var fx = await SeedAsync(db);

        // Dòng đã bị đính chính ⇒ bỏ CẢ sự kiện lắp lẫn tháo.
        db.AssetInstallations.Add(LedgerRow(fx, null, "Vị trí sai", Base.AddDays(1), Base.AddDays(2),
            supersededAt: Base.AddDays(3)));

        // ActionLog đã soft-delete + ghi cho THIẾT BỊ KHÁC ⇒ bỏ.
        db.ActionLogs.Add(new ActionLog
        {
            ItemType = ItemType.Asset,
            ItemId = fx.Asset.Id,
            ActionType = ActionType.Audit,
            CreatedBy = fx.Creator.Id,
            ActionDate = Base.AddDays(4),
            DeletedAt = Base.AddDays(5)
        });
        db.ActionLogs.Add(new ActionLog
        {
            ItemType = ItemType.Asset,
            ItemId = Guid.NewGuid(),
            ActionType = ActionType.Audit,
            CreatedBy = fx.Creator.Id,
            ActionDate = Base.AddDays(4)
        });
        db.ActionLogs.Add(new ActionLog
        {
            // ItemType khác (phiếu bảo dưỡng) ⇒ không thuộc timeline thiết bị.
            ItemType = ItemType.AssetMaintenance,
            ItemId = fx.Asset.Id,
            ActionType = ActionType.Create,
            CreatedBy = fx.Creator.Id,
            ActionDate = Base.AddDays(4)
        });

        // Phiếu bảo dưỡng đã soft-delete ⇒ bỏ.
        db.AssetMaintenances.Add(new AssetMaintenance
        {
            AssetId = fx.Asset.Id,
            Title = "Phiếu đã xoá",
            CompanyId = Guid.NewGuid(),
            StartDate = Base.AddDays(6),
            CreatedById = fx.Creator.Id,
            DeletedAt = Base.AddDays(7)
        });

        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Empty(result.Events!);
    }

    // ==================== isEstimated ====================

    [Fact]
    public async Task Timeline_IsEstimated_TrueOnlyForBackfillInstallEvent()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_IsEstimated_TrueOnlyForBackfillInstallEvent));
        var fx = await SeedAsync(db);

        // Backfill còn mở ⇒ sự kiện LẮP là ước tính.
        db.AssetInstallations.Add(LedgerRow(fx, null, "Vị trí A", Base.AddDays(1),
            source: AssetInstallationSource.Backfill, loggedByName: null));

        // Backfill đã đóng ⇒ LẮP ước tính, THÁO thì KHÔNG (mốc tháo đến từ thao tác thật).
        db.AssetInstallations.Add(LedgerRow(fx, null, "Vị trí B", Base.AddDays(2), Base.AddDays(5),
            source: AssetInstallationSource.Backfill, loggedByName: null));

        // Dòng API ⇒ cả hai sự kiện đều không ước tính.
        db.AssetInstallations.Add(LedgerRow(fx, null, "Vị trí C", Base.AddDays(3), Base.AddDays(4)));

        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50), CancellationToken.None);

        Assert.True(result.Success);
        var events = result.Events!;

        var backfillOpen = events.Single(e => e.EventType == "INSTALL" && e.Title.Contains("Vị trí A"));
        Assert.True(backfillOpen.IsEstimated);
        Assert.Null(backfillOpen.ActorName);   // backfill không có người ghi

        var backfillClosedInstall = events.Single(e => e.EventType == "INSTALL" && e.Title.Contains("Vị trí B"));
        var backfillClosedRemove = events.Single(e => e.EventType == "REMOVE" && e.Title.Contains("Vị trí B"));
        Assert.True(backfillClosedInstall.IsEstimated);
        Assert.False(backfillClosedRemove.IsEstimated);

        var apiInstall = events.Single(e => e.EventType == "INSTALL" && e.Title.Contains("Vị trí C"));
        var apiRemove = events.Single(e => e.EventType == "REMOVE" && e.Title.Contains("Vị trí C"));
        Assert.False(apiInstall.IsEstimated);
        Assert.False(apiRemove.IsEstimated);
    }

    // ==================== Phân trang cursor ====================

    [Fact]
    public async Task Timeline_CursorPagination_WithTiedTimestamps_NoLossNoDuplication()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_CursorPagination_WithTiedTimestamps_NoLossNoDuplication));
        var fx = await SeedAsync(db, campaignStart: Base);

        // 6 sự kiện CÙNG một mốc thời gian: 1 dòng lý lịch đã đóng có StartedAt == EndedAt
        // (⇒ 2 sự kiện trùng cả (OccurredAt, Id) — đây chính là ca cần Kind phá thế bằng nhau),
        // 2 ActionLog, 1 đợt bảo dưỡng, 1 phiếu bảo dưỡng.
        db.AssetInstallations.Add(LedgerRow(fx, null, "Vị trí trùng mốc", Base, Base));
        db.ActionLogs.Add(new ActionLog
        {
            ItemType = ItemType.Asset,
            ItemId = fx.Asset.Id,
            ActionType = ActionType.Audit,
            CreatedBy = fx.Creator.Id,
            ActionDate = Base
        });
        db.ActionLogs.Add(new ActionLog
        {
            ItemType = ItemType.Asset,
            ItemId = fx.Asset.Id,
            ActionType = ActionType.Update,
            CreatedBy = fx.Creator.Id,
            ActionDate = Base
        });
        db.MaintenanceCampaignDeviceSnapshots.Add(new MaintenanceCampaignDeviceSnapshot
        {
            CampaignId = fx.Campaign.Id,
            AssetId = fx.Asset.Id,
            AssetTag = fx.Asset.AssetTag,
            AssetName = fx.Asset.Name,
            SystemPositionName = "Vị trí trùng mốc",
            CreatedAt = Base
        });
        db.AssetMaintenances.Add(new AssetMaintenance
        {
            AssetId = fx.Asset.Id,
            Title = "Bảo trì cùng mốc",
            CompanyId = Guid.NewGuid(),
            StartDate = Base,
            CreatedById = fx.Creator.Id
        });
        await db.SaveChangesAsync();

        // Trang đầy đủ (không phân trang) làm chuẩn.
        var full = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50), CancellationToken.None);
        Assert.True(full.Success);
        Assert.Equal(6, full.Events!.Count);

        static string Key(AssetTimelineEventDto e) => $"{e.EventType}|{e.RefType}|{e.RefId}";
        var expected = full.Events.Select(Key).ToArray();
        Assert.Equal(6, expected.Distinct().Count());

        // Đi từng trang limit = 2 bằng con trỏ.
        var collected = new List<string>();
        string? cursor = null;
        var guard = 0;
        while (true)
        {
            var page = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, cursor, 2), CancellationToken.None);
            Assert.True(page.Success);
            Assert.True(page.Events!.Count <= 2);

            collected.AddRange(page.Events.Select(Key));
            Assert.True(++guard <= 10, "phân trang không kết thúc");

            if (page.NextCursor == null)
                break;

            cursor = page.NextCursor;
        }

        // KHÔNG mất, KHÔNG lặp, ĐÚNG thứ tự như trang đầy đủ.
        Assert.Equal(expected, collected);
    }

    [Fact]
    public async Task Timeline_ReturnsNextCursor_WhenMoreEventsExist()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_ReturnsNextCursor_WhenMoreEventsExist));
        var fx = await SeedAsync(db);

        for (var i = 0; i < 5; i++)
        {
            db.ActionLogs.Add(new ActionLog
            {
                ItemType = ItemType.Asset,
                ItemId = fx.Asset.Id,
                ActionType = ActionType.Audit,
                CreatedBy = fx.Creator.Id,
                ActionDate = Base.AddDays(i)
            });
        }

        await db.SaveChangesAsync();

        var first = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 2), CancellationToken.None);
        Assert.True(first.Success);
        Assert.Equal(2, first.Events!.Count);
        Assert.NotNull(first.NextCursor);

        var second = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, first.NextCursor, 2), CancellationToken.None);
        Assert.True(second.Success);
        Assert.Equal(2, second.Events!.Count);
        Assert.NotNull(second.NextCursor);

        // Trang kế tiếp không chứa sự kiện của trang trước.
        Assert.Empty(second.Events.Select(e => e.RefId).Intersect(first.Events.Select(e => e.RefId)));
    }

    // ==================== Phạm vi công ty + con trỏ + limit ====================

    [Fact]
    public async Task Timeline_NotFound_WhenAssetOutsideCompanyScope()
    {
        var assetCompany = Guid.NewGuid();
        var otherCompany = Guid.NewGuid();

        await using var db = TestHelpers.CreateContext(
            nameof(Timeline_NotFound_WhenAssetOutsideCompanyScope),
            new TestHelpers.FakeScope { CompanyId = otherCompany });
        var fx = await SeedAsync(db, assetCompanyId: assetCompany);

        var result = await Handler(db, new TestHelpers.FakeScope { CompanyId = otherCompany })
            .Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.ErrorCode);           // 404 (không phải lỗi nghiệp vụ)

        // Cùng asset đó, đúng công ty ⇒ thấy được.
        var inScope = await Handler(db, new TestHelpers.FakeScope { CompanyId = assetCompany })
            .Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50), CancellationToken.None);
        Assert.True(inScope.Success);
    }

    [Fact]
    public async Task Timeline_NotFound_WhenAssetDoesNotExist()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_NotFound_WhenAssetDoesNotExist));

        var result = await Handler(db).Handle(new AssetTimelineQuery(Guid.NewGuid(), null, 50), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task Timeline_InvalidCursor_ReturnsErrorCode()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_InvalidCursor_ReturnsErrorCode));
        var fx = await SeedAsync(db);

        var result = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, "khong-phai-cursor", 50), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(AssetTimelineQuery.InvalidCursorErrorCode, result.ErrorCode);
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(-7, 50)]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(200, 200)]
    [InlineData(201, 200)]
    [InlineData(5000, 200)]
    public void NormalizeLimit_ClampsToDefaultAndMax(int input, int expected)
    {
        Assert.Equal(expected, AssetTimelineQueryHandler.NormalizeLimit(input));
    }

    [Fact]
    public async Task Timeline_UsesNormalizedLimit()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_UsesNormalizedLimit));
        var fx = await SeedAsync(db);

        var zero = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 0), CancellationToken.None);
        Assert.True(zero.Success);
        Assert.Equal(AssetTimelineQueryHandler.DefaultLimit, zero.Limit);

        var huge = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 100000), CancellationToken.None);
        Assert.True(huge.Success);
        Assert.Equal(AssetTimelineQueryHandler.MaxLimit, huge.Limit);
    }

    [Fact]
    public void Cursor_RoundTrip_PreservesOccurredAtIdAndKind()
    {
        var original = new AssetTimelineCursor(new DateTime(2026, 3, 1, 8, 30, 15, 123, DateTimeKind.Utc), Guid.NewGuid(), 4);

        var encoded = original.Encode();
        Assert.DoesNotContain("=", encoded);          // base64url, không padding
        Assert.True(AssetTimelineCursor.TryDecode(encoded, out var decoded));

        Assert.Equal(original.OccurredAt, decoded.OccurredAt);
        Assert.Equal(original.OccurredAt.Kind, decoded.OccurredAt.Kind);
        Assert.Equal(original.Id, decoded.Id);
        Assert.Equal(original.Kind, decoded.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("khong-phai-base64")]
    [InlineData("Zm9v")]                      // base64 hợp lệ nhưng không đúng định dạng
    [InlineData("MTIzfHh8NDU2")]              // ticks|không-phải-guid|kind
    public void Cursor_TryDecode_RejectsInvalidInput(string? value)
    {
        Assert.False(AssetTimelineCursor.TryDecode(value, out _));
    }

    /// <summary>
    /// Canary DateTime Kind — bài học Postgres THẬT: lật sang trang 2 ném 500
    /// <c>Cannot write DateTime with Kind=UTC to PostgreSQL type 'timestamp without time zone'</c> vì
    /// <c>asset_maintenances.StartDate</c> là <c>timestamp without time zone</c> (InMemory không phát hiện).
    /// </summary>
    [Fact]
    public void MaintenanceTimestampKind_IsNormalizedForColumnAndForApi()
    {
        var utc = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);

        // Tham số so sánh trong SQL: PHẢI là Unspecified (giữ nguyên giá trị, chỉ đổi Kind).
        var forColumn = AssetTimelineQueryHandler.ToColumnKind(utc);
        Assert.Equal(DateTimeKind.Unspecified, forColumn.Kind);
        Assert.Equal(utc.Ticks, forColumn.Ticks);

        // Giá trị trả ra API: PHẢI là UTC (JSON có 'Z' — trộn 4 nguồn cùng một quy ước).
        var forApi = AssetTimelineQueryHandler.ToApiKind(new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Unspecified));
        Assert.Equal(DateTimeKind.Utc, forApi.Kind);
        Assert.Equal(utc.Ticks, forApi.Ticks);
    }

    [Fact]
    public async Task Timeline_MaintenanceEvent_OccurredAt_IsUtc()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_MaintenanceEvent_OccurredAt_IsUtc));
        var fx = await SeedAsync(db);

        db.AssetMaintenances.Add(new AssetMaintenance
        {
            AssetId = fx.Asset.Id,
            Title = "Phiếu Kind",
            CompanyId = Guid.NewGuid(),
            StartDate = new DateTime(2026, 3, 4, 10, 30, 0, DateTimeKind.Unspecified),
            CreatedById = fx.Creator.Id
        });
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50), CancellationToken.None);

        Assert.True(result.Success);
        var maint = Assert.Single(result.Events!);
        Assert.Equal("MAINTENANCE", maint.EventType);
        Assert.Equal(DateTimeKind.Utc, maint.OccurredAt.Kind);
        Assert.Equal(new DateTime(2026, 3, 4, 10, 30, 0, DateTimeKind.Utc), maint.OccurredAt);
    }

    /// <summary>
    /// [LL-3][Hợp đồng DTO] Ngày hoàn thành của phiếu bảo dưỡng trả ở trường CÓ CẤU TRÚC (UTC, JSON có 'Z');
    /// <c>null</c> = phiếu CHƯA hoàn thành (server không gửi chuỗi "Chưa hoàn thành" và không format "Bắt đầu: ...").
    /// Cột <c>CompletionDate</c> là <c>timestamp without time zone</c> ⇒ đây là canary Kind thứ hai như <c>StartDate</c>.
    /// </summary>
    [Fact]
    public async Task Timeline_MaintenanceEvent_CompletionDate_IsUtcOrNull()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_MaintenanceEvent_CompletionDate_IsUtcOrNull));
        var fx = await SeedAsync(db);

        // Npgsql trả cột `timestamp without time zone` với Kind=Unspecified ⇒ mô phỏng đúng dữ liệu thật.
        db.AssetMaintenances.Add(new AssetMaintenance
        {
            AssetId = fx.Asset.Id,
            Title = "Đã xong",
            Type = AssetMaintenanceType.Repair,
            CompanyId = Guid.NewGuid(),
            StartDate = new DateTime(2026, 3, 4, 10, 30, 0, DateTimeKind.Unspecified),
            CompletionDate = new DateTime(2026, 3, 9, 16, 45, 0, DateTimeKind.Unspecified),
            CreatedById = fx.Creator.Id
        });
        db.AssetMaintenances.Add(new AssetMaintenance
        {
            AssetId = fx.Asset.Id,
            Title = "Chưa xong",
            Type = AssetMaintenanceType.Maintenance,
            CompanyId = Guid.NewGuid(),
            StartDate = new DateTime(2026, 3, 5, 8, 0, 0, DateTimeKind.Unspecified),
            CreatedById = fx.Creator.Id
        });
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50), CancellationToken.None);

        Assert.True(result.Success);
        var done = result.Events!.Single(e => e.Title.Contains("Đã xong"));
        var undone = result.Events!.Single(e => e.Title.Contains("Chưa xong"));

        Assert.NotNull(done.MaintenanceCompletionDate);
        Assert.Equal(DateTimeKind.Utc, done.MaintenanceCompletionDate!.Value.Kind);
        Assert.Equal(new DateTime(2026, 3, 9, 16, 45, 0, DateTimeKind.Utc), done.MaintenanceCompletionDate.Value);

        // Mốc BẮT ĐẦU của phiếu = OccurredAt (UTC) — không có trường riêng, không lặp trong Details.
        Assert.Equal(new DateTime(2026, 3, 4, 10, 30, 0, DateTimeKind.Utc), done.OccurredAt);

        Assert.Null(undone.MaintenanceCompletionDate);
        Assert.Null(done.Details);
        Assert.Null(undone.Details);
    }

    /// <summary>
    /// [LL-3][Hợp đồng DTO] Server KHÔNG bao giờ format ngày/giờ thành chuỗi trong <c>details</c> — quét cả 4 nguồn
    /// để chặn việc thêm lại chuỗi ngày (mọi mốc hiển thị phải là trường có cấu trúc UTC cho FE tự format).
    /// </summary>
    [Fact]
    public async Task Timeline_Details_NeverContainFormattedDates()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_Details_NeverContainFormattedDates));
        var fx = await SeedAsync(db);
        var position = new SystemPosition { Code = "LL3-POS", Name = "Vị trí 1", SystemInfoId = fx.System.Id };
        db.SystemPositions.Add(position);

        db.AssetInstallations.Add(LedgerRow(fx, position.Id, position.Name, Base.AddDays(1), Base.AddDays(2)));
        db.ActionLogs.Add(new ActionLog
        {
            ItemType = ItemType.Asset,
            ItemId = fx.Asset.Id,
            ActionType = ActionType.Audit,
            CreatedBy = fx.Creator.Id,
            ActionDate = Base.AddDays(3),
            LocationName = "Kho A",
            Note = "kiểm kê định kỳ"
        });
        db.MaintenanceCampaignDeviceSnapshots.Add(new MaintenanceCampaignDeviceSnapshot
        {
            CampaignId = fx.Campaign.Id,
            AssetId = fx.Asset.Id,
            AssetTag = fx.Asset.AssetTag,
            AssetName = fx.Asset.Name,
            SystemPositionName = position.Name,
            CreatedAt = Base.AddDays(4)
        });
        db.AssetMaintenances.Add(new AssetMaintenance
        {
            AssetId = fx.Asset.Id,
            Title = "Thay quạt",
            Type = AssetMaintenanceType.Repair,
            CompanyId = Guid.NewGuid(),
            StartDate = Base.AddDays(5),
            CompletionDate = Base.AddDays(6),
            CreatedById = fx.Creator.Id
        });
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50), CancellationToken.None);

        Assert.True(result.Success);
        // Dòng lý lịch đã đóng ⇒ 2 sự kiện (INSTALL + REMOVE) + ActionLog + đợt bảo dưỡng + phiếu bảo dưỡng = 5.
        Assert.Equal(5, result.Events!.Count);
        // Cả 4 nguồn đều góp mặt (so theo tập, không phụ thuộc thứ tự).
        Assert.Equal(
            new[] { "INSTALL", "REMOVE", "AUDIT", "MAINTENANCE_CAMPAIGN", "MAINTENANCE" }.OrderBy(x => x).ToArray(),
            result.Events.Select(e => e.EventType).OrderBy(x => x).ToArray());
        Assert.All(result.Events, e =>
        {
            if (e.Details == null)
                return;

            Assert.DoesNotMatch(@"\d{1,2}/\d{1,2}/\d{2,4}", e.Details);   // dd/MM/yyyy (kiểu VN)
            Assert.DoesNotMatch(@"\d{4}-\d{2}-\d{2}", e.Details);         // yyyy-MM-dd (kiểu ISO)
            Assert.DoesNotContain("Bắt đầu", e.Details);
        });
    }

    /// <summary>
    /// [LL-3][Hợp đồng DTO] Sự kiện MAINTENANCE_CAMPAIGN: mốc "bắt đầu đợt" CHÍNH LÀ <c>occurredAt</c> (UTC) —
    /// không còn chuỗi "Bắt đầu đợt: dd/MM/yyyy" trong <c>details</c>.
    /// </summary>
    [Fact]
    public async Task Timeline_CampaignEvent_StartDateIsOccurredAt_NotADetailsString()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_CampaignEvent_StartDateIsOccurredAt_NotADetailsString));
        var fx = await SeedAsync(db);   // Campaign.StartDate = Base + 3 ngày

        db.MaintenanceCampaignDeviceSnapshots.Add(new MaintenanceCampaignDeviceSnapshot
        {
            CampaignId = fx.Campaign.Id,
            AssetId = fx.Asset.Id,
            AssetTag = fx.Asset.AssetTag,
            AssetName = fx.Asset.Name,
            SystemPositionName = "Vị trí đợt",
            CreatedAt = Base
        });
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50), CancellationToken.None);

        var only = Assert.Single(result.Events!);
        Assert.Equal("MAINTENANCE_CAMPAIGN", only.EventType);
        Assert.Equal(DateTimeKind.Utc, only.OccurredAt.Kind);
        Assert.Equal(fx.Campaign.StartDate, only.OccurredAt);           // mốc nghiệp vụ của đợt
        Assert.DoesNotContain("Bắt đầu đợt", only.Details!);
        Assert.Contains("Trạng thái đợt", only.Details!);
    }

    /// <summary>
    /// [LL-3][Hợp đồng DTO] Kiểm ở tầng JSON (Web defaults — camelCase, giống API): mốc thời gian phải có hậu tố
    /// 'Z' (UTC) và <c>details</c> phải là null/không chứa ngày đã format.
    /// </summary>
    [Fact]
    public async Task Timeline_Json_ExposesZuluTimestamps_AndNoDateStringInDetails()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_Json_ExposesZuluTimestamps_AndNoDateStringInDetails));
        var fx = await SeedAsync(db);

        db.AssetMaintenances.Add(new AssetMaintenance
        {
            AssetId = fx.Asset.Id,
            Title = "Kiểm JSON",
            Type = AssetMaintenanceType.Repair,
            CompanyId = Guid.NewGuid(),
            StartDate = new DateTime(2026, 3, 4, 10, 30, 0, DateTimeKind.Unspecified),
            CompletionDate = new DateTime(2026, 3, 9, 16, 45, 0, DateTimeKind.Unspecified),
            CreatedById = fx.Creator.Id
        });
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50), CancellationToken.None);
        var json = System.Text.Json.JsonSerializer.Serialize(
            result, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var ev = doc.RootElement.GetProperty("events")[0];

        Assert.Equal("MAINTENANCE", ev.GetProperty("eventType").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, ev.GetProperty("details").ValueKind);

        var occurredAt = ev.GetProperty("occurredAt").GetString()!;
        Assert.EndsWith("Z", occurredAt);

        var completion = ev.GetProperty("maintenanceCompletionDate").GetString()!;
        Assert.EndsWith("Z", completion);
        Assert.StartsWith("2026-03-09T16:45:00", completion);
    }

    [Fact]
    public async Task Timeline_CampaignEvent_ActorIsResolved_EvenAloneOnPage()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_CampaignEvent_ActorIsResolved_EvenAloneOnPage));
        var fx = await SeedAsync(db);

        // CHỈ có 1 sự kiện trong DB ⇒ trang limit=1 chỉ chứa nó. Tên người phụ trách đợt vẫn phải tra được
        // (bug thật trên Postgres: với limit=1 actorName = null, limit=3 lại có tên, vì id chỉ lọt vào lô
        // tra cứu khi có sự kiện KHÁC trong trang cùng người).
        db.MaintenanceCampaignDeviceSnapshots.Add(new MaintenanceCampaignDeviceSnapshot
        {
            CampaignId = fx.Campaign.Id,
            AssetId = fx.Asset.Id,
            AssetTag = fx.Asset.AssetTag,
            AssetName = fx.Asset.Name,
            SystemPositionName = "Vị trí một mình",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 1), CancellationToken.None);

        Assert.True(result.Success);
        var only = Assert.Single(result.Events!);
        Assert.Equal("MAINTENANCE_CAMPAIGN", only.EventType);
        Assert.Contains("Vị trí một mình", only.Details!);
        Assert.Equal("Kiểm Thử", only.ActorName);
    }

    /// <summary>
    /// Canary: kỹ thuật phân trang của timeline (so sánh Guid NGAY TRONG SQL + ORDER BY Guid) phải dịch
    /// được sang SQL của Npgsql. Nếu ai đó đổi sang dạng không dịch được, test này đổ ngay mà KHÔNG cần
    /// Postgres thật (ToQueryString chỉ dịch, không kết nối).
    /// </summary>
    [Fact]
    public void CursorTechnique_Translates_OnNpgsql()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=canary;Username=canary;Password=canary")
            .Options;
        using var db = new AppDbContext(options, new TestHelpers.SuperUserScope());

        var assetId = Guid.NewGuid();
        var at = Base;
        var id = Guid.NewGuid();

        var sql = db.AssetInstallations.AsNoTracking()
            .Where(i => i.AssetId == assetId && i.SupersededAt == null
                        && (i.StartedAt < at || (i.StartedAt == at && i.Id.CompareTo(id) < 0)))
            .OrderByDescending(i => i.StartedAt)
            .ThenByDescending(i => i.Id)
            .Take(51)
            .ToQueryString();

        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"Id\" <", sql);   // so sánh uuid ở phía SQL (không kéo về client)

        // Nguồn "đợt bảo dưỡng" lọc/sắp xếp trên navigation Campaign.StartDate ⇒ phải dịch thành JOIN.
        var campaignsSql = db.MaintenanceCampaignDeviceSnapshots.AsNoTracking()
            .Where(s => s.AssetId == assetId && (s.Campaign.StartDate < at || (s.Campaign.StartDate == at && s.Id.CompareTo(id) < 0)))
            .OrderByDescending(s => s.Campaign.StartDate)
            .ThenByDescending(s => s.Id)
            .Take(51)
            .ToQueryString();

        Assert.Contains("JOIN", campaignsSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"StartDate\"", campaignsSql);

        // [LL-4b] Vị từ lọc theo `types` (HashSet<ActionType> ⇒ IN/ANY phía SQL) cũng phải dịch được — nếu
        // không, endpoint 500 trên Postgres thật trong khi InMemory vẫn xanh (đúng bài học BUG-Q).
        var allowedActionTypes = new HashSet<ActionType> { ActionType.Create, ActionType.Update, ActionType.UpdateRejected };
        var filterSql = db.ActionLogs.AsNoTracking()
            .Where(l => l.ItemType == ItemType.Asset
                        && l.ItemId == assetId
                        && l.DeletedAt == null
                        && !(l.TargetType == AssignmentTargetType.SystemPosition
                             && (l.ActionType == ActionType.Checkout || l.ActionType == ActionType.Checkin))
                        && (allowedActionTypes == null || allowedActionTypes.Contains(l.ActionType)))
            .OrderByDescending(l => l.ActionDate)
            .ThenByDescending(l => l.Id)
            .Take(51)
            .ToQueryString();

        Assert.True(
            filterSql.Contains("IN (", StringComparison.OrdinalIgnoreCase)
            || filterSql.Contains("ANY", StringComparison.OrdinalIgnoreCase),
            "Bộ lọc `types` phải dịch thành IN/ANY ở phía SQL:\n" + filterSql);
    }

    // ==================== [LL-4b] Lọc `types` + `id` + tiêu đề/details ====================

    /// <summary>Seed 1 thiết bị có ĐỦ 5 sự kiện thuộc 4 loại: INSTALL, REMOVE, AUDIT, MAINTENANCE_CAMPAIGN, MAINTENANCE.</summary>
    private static async Task<Fixture> SeedAllEventTypesAsync(AppDbContext db)
    {
        var fx = await SeedAsync(db);
        var position = new SystemPosition { Code = "LL4B-POS", Name = "Vị trí 1", SystemInfoId = fx.System.Id };
        db.SystemPositions.Add(position);
        db.AssetInstallations.Add(LedgerRow(fx, position.Id, position.Name, Base.AddDays(1), Base.AddDays(2)));
        db.ActionLogs.Add(new ActionLog
        {
            ItemType = ItemType.Asset,
            ItemId = fx.Asset.Id,
            ActionType = ActionType.Audit,
            CreatedBy = fx.Creator.Id,
            ActionDate = Base.AddDays(3),
            LocationName = "Kho A",
            Note = "kiểm kê định kỳ"
        });
        db.MaintenanceCampaignDeviceSnapshots.Add(new MaintenanceCampaignDeviceSnapshot
        {
            CampaignId = fx.Campaign.Id,
            AssetId = fx.Asset.Id,
            AssetTag = fx.Asset.AssetTag,
            AssetName = fx.Asset.Name,
            SystemPositionName = position.Name,
            CreatedAt = Base.AddDays(4)
        });
        db.AssetMaintenances.Add(new AssetMaintenance
        {
            AssetId = fx.Asset.Id,
            Title = "Thay quạt",
            Type = AssetMaintenanceType.Repair,
            CompanyId = Guid.NewGuid(),
            StartDate = Base.AddDays(5),
            CompletionDate = Base.AddDays(6),
            CreatedById = fx.Creator.Id
        });
        await db.SaveChangesAsync();
        return fx;
    }

    /// <summary>[LL-4b] `types` lọc theo eventType — chỉ trả về đúng các loại được yêu cầu.</summary>
    [Fact]
    public async Task Timeline_TypesFilter_ReturnsOnlyRequestedTypes()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_TypesFilter_ReturnsOnlyRequestedTypes));
        var fx = await SeedAllEventTypesAsync(db);

        // Không lọc ⇒ đủ 5 sự kiện (đối chiếu để thấy bộ lọc THẬT SỰ có tác dụng).
        var all = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50), CancellationToken.None);
        Assert.Equal(5, all.Events!.Count);

        // Tập "Sự kiện chính" của FE (LL-4b yêu cầu 6) — 4 loại riêng của lý lịch, KHÔNG có AUDIT.
        var main = await Handler(db).Handle(
            new AssetTimelineQuery(fx.Asset.Id, null, 50, new[] { "INSTALL", "REMOVE", "MAINTENANCE", "MAINTENANCE_CAMPAIGN" }),
            CancellationToken.None);
        Assert.Equal(
            new[] { "INSTALL", "MAINTENANCE", "MAINTENANCE_CAMPAIGN", "REMOVE" },
            main.Events!.Select(e => e.EventType).OrderBy(x => x).ToArray());

        // Chỉ AUDIT (sự kiện đến từ ActionLog).
        var audit = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50, new[] { "AUDIT" }), CancellationToken.None);
        var only = Assert.Single(audit.Events!);
        Assert.Equal("AUDIT", only.EventType);

        // `types` rỗng ⇒ KHÔNG lọc (không phải lỗi).
        var empty = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50, new[] { "" }), CancellationToken.None);
        Assert.Equal(5, empty.Events!.Count);
    }

    /// <summary>
    /// [LL-4b] Lọc + phân trang CURSOR: đi từng trang (limit=1) với bộ lọc phải thu được ĐÚNG chuỗi sự kiện
    /// của một trang lớn — KHÔNG mất, KHÔNG lặp (bộ lọc phải nằm ở WHERE của từng nguồn, không phải lọc sau).
    /// </summary>
    [Fact]
    public async Task Timeline_TypesFilter_WithCursorPagination_NoLossNoDuplication()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_TypesFilter_WithCursorPagination_NoLossNoDuplication));
        var fx = await SeedAllEventTypesAsync(db);
        for (var i = 0; i < 5; i++)
        {
            db.ActionLogs.Add(new ActionLog
            {
                ItemType = ItemType.Asset,
                ItemId = fx.Asset.Id,
                ActionType = ActionType.Audit,
                CreatedBy = fx.Creator.Id,
                ActionDate = Base.AddDays(10 + i),
                Note = $"kiểm kê {i}"
            });
        }

        await db.SaveChangesAsync();

        var filter = new[] { "AUDIT", "INSTALL" };

        var full = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50, filter), CancellationToken.None);
        Assert.Equal(7, full.Events!.Count);   // 6 AUDIT + 1 INSTALL (các loại khác bị lọc bỏ)
        var expectedIds = full.Events.Select(e => e.Id).ToList();

        var collected = new List<string>();
        string? cursor = null;
        var guard = 0;
        do
        {
            var page = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, cursor, 1, filter), CancellationToken.None);
            Assert.True(page.Success);
            collected.AddRange(page.Events!.Select(e => e.Id));
            cursor = page.NextCursor;
        } while (cursor != null && ++guard < 50);

        Assert.Null(cursor);
        Assert.Equal(expectedIds, collected);
        Assert.Equal(collected.Count, collected.Distinct().Count());
    }

    /// <summary>[LL-4b] `types` chứa giá trị lạ ⇒ 400 INVALID_TYPES (không im lặng bỏ qua).</summary>
    [Fact]
    public async Task Timeline_TypesFilter_UnknownValue_ReturnsInvalidTypesError()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_TypesFilter_UnknownValue_ReturnsInvalidTypesError));
        var fx = await SeedAsync(db);

        var result = await Handler(db).Handle(
            new AssetTimelineQuery(fx.Asset.Id, null, 50, new[] { "INSTALL", "KHONG-CO" }), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(AssetTimelineQuery.InvalidTypesErrorCode, result.ErrorCode);
        Assert.Contains("KHONG-CO", result.Message!);
        Assert.Null(result.Events);
    }

    /// <summary>[LL-4b] `types` nhận tên ActionType dạng SCREAMING_SNAKE, không phân biệt hoa thường, và cả dạng có dấu phẩy.</summary>
    [Fact]
    public async Task Timeline_TypesFilter_AcceptsActionTypeNames_CaseInsensitiveAndCommaSeparated()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_TypesFilter_AcceptsActionTypeNames_CaseInsensitiveAndCommaSeparated));
        var fx = await SeedAsync(db);
        db.ActionLogs.Add(new ActionLog
        {
            ItemType = ItemType.Asset,
            ItemId = fx.Asset.Id,
            ActionType = ActionType.Checkout,
            CreatedBy = fx.Creator.Id,
            ActionDate = Base.AddDays(1),
            TargetType = AssignmentTargetType.User,
            TargetId = fx.Creator.Id
        });
        db.ActionLogs.Add(new ActionLog { ItemType = ItemType.Asset, ItemId = fx.Asset.Id, ActionType = ActionType.Update, CreatedBy = fx.Creator.Id, ActionDate = Base.AddDays(2) });
        db.ActionLogs.Add(new ActionLog { ItemType = ItemType.Asset, ItemId = fx.Asset.Id, ActionType = ActionType.UpdateRejected, CreatedBy = fx.Creator.Id, ActionDate = Base.AddDays(3) });
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new AssetTimelineQuery(fx.Asset.Id, null, 50, new[] { "checkout,UPDATE", "UPDATE_REJECTED" }), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(
            new[] { "CHECKOUT", "UPDATE", "UPDATE_REJECTED" },
            result.Events!.Select(e => e.EventType).OrderBy(x => x).ToArray());
    }

    /// <summary>[LL-4b] Chuẩn hoá tham số <c>types</c> (đơn vị nhỏ nhất, không cần dựng DB).</summary>
    [Theory]
    [InlineData(null, true, true)]
    [InlineData("", true, true)]
    [InlineData("   ", true, true)]
    [InlineData("INSTALL", true, false)]
    [InlineData("install,remove", true, false)]
    [InlineData("MAINTENANCE", true, false)]
    [InlineData("UPDATE_REJECTED", true, false)]
    [InlineData("khong-co-loai-nay", false, false)]
    public void TypesFilter_TryCreate_NormalizesAndValidates(string? raw, bool expectedOk, bool expectNoFilter)
    {
        var input = raw is null ? null : new[] { raw };

        var ok = AssetTimelineTypeFilter.TryCreate(input, out var filter, out var unknown);

        Assert.Equal(expectedOk, ok);
        if (expectedOk)
            Assert.Equal(expectNoFilter, filter == null);
        else
            Assert.Contains("KHONG-CO-LOAI-NAY", unknown);

        Assert.Contains("INSTALL", AssetTimelineTypeFilter.KnownValues);
        Assert.Contains("CHECKOUT", AssetTimelineTypeFilter.KnownValues);
    }

    /// <summary>[LL-4b] Tiêu đề lắp/tháo dùng dạng ngắn, KHÔNG lặp chữ "vị trí".</summary>
    [Fact]
    public async Task Timeline_InstallAndRemoveTitles_UseShortForm()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_InstallAndRemoveTitles_UseShortForm));
        var fx = await SeedAllEventTypesAsync(db);

        var result = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50), CancellationToken.None);

        var install = result.Events!.Single(e => e.EventType == "INSTALL");
        var remove = result.Events!.Single(e => e.EventType == "REMOVE");
        Assert.Equal("Lắp đặt tại: Vị trí 1", install.Title);
        Assert.Equal("Tháo khỏi: Vị trí 1", remove.Title);
        Assert.DoesNotContain("vị trí Vị trí", install.Title);
        Assert.DoesNotContain("vị trí Vị trí", remove.Title);
    }

    /// <summary>
    /// [LL-4b] Dòng <c>Source = Backfill</c>: Note kỹ thuật KHÔNG lộ ra details (cả sự kiện lắp LẪN sự kiện tháo),
    /// trong khi dòng do API ghi vẫn hiện Note; và dữ liệu Note trong DB KHÔNG bị sửa.
    /// </summary>
    [Fact]
    public async Task Timeline_BackfillRows_DoNotExposeTechnicalNote_AndDbDataStaysIntact()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_BackfillRows_DoNotExposeTechnicalNote_AndDbDataStaysIntact));
        var fx = await SeedAsync(db);
        var posA = new SystemPosition { Code = "LL4B-A", Name = "Vị trí A", SystemInfoId = fx.System.Id };
        var posB = new SystemPosition { Code = "LL4B-B", Name = "Vị trí B", SystemInfoId = fx.System.Id };
        var posC = new SystemPosition { Code = "LL4B-C", Name = "Vị trí C", SystemInfoId = fx.System.Id };
        db.SystemPositions.AddRange(posA, posB, posC);

        var backfillOpen = LedgerRow(fx, posA.Id, posA.Name, Base.AddDays(1), source: AssetInstallationSource.Backfill, loggedByName: null);
        var backfillClosed = LedgerRow(fx, posB.Id, posB.Name, Base.AddDays(2), Base.AddDays(3), AssetInstallationSource.Backfill, loggedByName: null);
        var apiRow = LedgerRow(fx, posC.Id, posC.Name, Base.AddDays(4));
        db.AssetInstallations.AddRange(backfillOpen, backfillClosed, apiRow);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50), CancellationToken.None);
        var events = result.Events!;
        Assert.Equal(4, events.Count);   // 3 INSTALL + 1 REMOVE

        var backfillInstall = events.Single(e => e.EventType == "INSTALL" && e.Title.Contains("Vị trí A"));
        Assert.True(backfillInstall.IsEstimated);
        Assert.NotNull(backfillInstall.Details);
        Assert.DoesNotContain("ghi chú lý lịch", backfillInstall.Details!);
        Assert.Contains("Hệ thống: Hệ thống LL-3", backfillInstall.Details!);

        var backfillRemove = events.Single(e => e.EventType == "REMOVE" && e.Title.Contains("Vị trí B"));
        Assert.False(backfillRemove.IsEstimated);
        Assert.NotNull(backfillRemove.Details);
        Assert.DoesNotContain("ghi chú lý lịch", backfillRemove.Details!);

        // Dòng do API ghi ⇒ Note vẫn hiện bình thường (không chặn oan).
        var apiInstall = events.Single(e => e.EventType == "INSTALL" && e.Title.Contains("Vị trí C"));
        Assert.Contains("ghi chú lý lịch", apiInstall.Details!);

        // KHÔNG sửa dữ liệu Note trong DB.
        var stored = await db.AssetInstallations.AsNoTracking().SingleAsync(i => i.Id == backfillOpen.Id);
        Assert.Equal("ghi chú lý lịch", stored.Note);
    }

    /// <summary>[LL-4b] Sự kiện CREATE: details tiếng Việt, KHÔNG lặp lại mã/tên thiết bị.</summary>
    [Fact]
    public async Task Timeline_CreateEvent_DetailsAreVietnameseAndDoNotRepeatAssetIdentity()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_CreateEvent_DetailsAreVietnameseAndDoNotRepeatAssetIdentity));
        var fx = await SeedAsync(db);
        db.ActionLogs.Add(new ActionLog
        {
            ItemType = ItemType.Asset,
            ItemId = fx.Asset.Id,
            ActionType = ActionType.Create,
            CreatedBy = fx.Creator.Id,
            ActionDate = Base,
            // Note gốc của CreateAssetCommand: tiếng Anh + lặp lại mã và tên thiết bị.
            Note = $"Created asset: {fx.Asset.AssetTag} - {fx.Asset.Name}",
            LogMeta = JsonSerializer.Serialize(new { assetTag = fx.Asset.AssetTag, name = fx.Asset.Name, serial = "SN-001" })
        });
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50), CancellationToken.None);
        var create = result.Events!.Single(e => e.EventType == "CREATE");

        Assert.Equal("Tạo mới", create.Title);
        Assert.Contains("Serial: SN-001", create.Details!);
        Assert.DoesNotContain("Created asset", create.Details!);
        Assert.DoesNotContain(fx.Asset.AssetTag, create.Details!);
        Assert.DoesNotContain(fx.Asset.Name, create.Details!);
    }

    /// <summary>[LL-4b] Sự kiện CREATE không có serial ⇒ không có details (không lặp lại Note tiếng Anh).</summary>
    [Fact]
    public async Task Timeline_CreateEvent_WithoutSerial_HasNoDetails()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_CreateEvent_WithoutSerial_HasNoDetails));
        var fx = await SeedAsync(db);
        db.ActionLogs.Add(new ActionLog
        {
            ItemType = ItemType.Asset,
            ItemId = fx.Asset.Id,
            ActionType = ActionType.Create,
            CreatedBy = fx.Creator.Id,
            ActionDate = Base,
            Note = $"Created asset: {fx.Asset.AssetTag} - {fx.Asset.Name}"
        });
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50), CancellationToken.None);
        var create = result.Events!.Single(e => e.EventType == "CREATE");

        Assert.Null(create.Details);
    }

    /// <summary>
    /// [LL-4b] Sự kiện UPDATE: details liệt kê field thay đổi từ <c>LogMeta.changes</c> — nhãn tiếng Việt,
    /// "cũ → mới", id tham chiếu được TRA TÊN; field ngày KHÔNG đưa giá trị vào details (quy ước LL-3).
    /// </summary>
    [Fact]
    public async Task Timeline_UpdateEvent_DetailsListChangedFieldsWithVietnameseLabels()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_UpdateEvent_DetailsListChangedFieldsWithVietnameseLabels));
        var fx = await SeedAsync(db);
        var location = new Location { Name = "Kho B" };
        db.Locations.Add(location);
        await db.SaveChangesAsync();

        db.ActionLogs.Add(new ActionLog
        {
            ItemType = ItemType.Asset,
            ItemId = fx.Asset.Id,
            ActionType = ActionType.Update,
            CreatedBy = fx.Creator.Id,
            ActionDate = Base,
            Note = "cập nhật thiết bị",
            LogMeta = JsonSerializer.Serialize(new
            {
                changes = new Dictionary<string, object?>
                {
                    ["name"] = new { old = "Tên cũ", @new = "Tên mới" },
                    ["serial"] = new { old = (string?)null, @new = "SN-9" },
                    ["location_id"] = new { old = (Guid?)null, @new = location.Id },
                    ["physical"] = new { old = false, @new = true },
                    ["purchase_date"] = new { old = (string?)null, @new = "2026-10-01T00:00:00Z" }
                }
            })
        });
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50), CancellationToken.None);
        var update = result.Events!.Single(e => e.EventType == "UPDATE");

        Assert.Equal("Cập nhật", update.Title);
        Assert.Contains("Tên: Tên cũ → Tên mới", update.Details!);
        Assert.Contains("Serial: (trống) → SN-9", update.Details!);
        Assert.Contains("Vị trí: (trống) → Kho B", update.Details!);      // id ⇒ tra ra TÊN, không hiện GUID
        Assert.Contains("Thiết bị vật lý: Không → Có", update.Details!);
        Assert.Contains("Ngày mua: (đã đổi)", update.Details!);          // field ngày: không có giá trị trong details
        Assert.DoesNotContain("cập nhật thiết bị", update.Details!);      // KHÔNG dùng lại Note gốc
        Assert.DoesNotContain(location.Id.ToString(), update.Details!);   // không lộ GUID
        Assert.DoesNotContain("2026", update.Details!);                   // không ngày trong details
    }

    /// <summary>[LL-4b] UPDATE không có changes (LogMeta null) ⇒ details mặc định.</summary>
    [Fact]
    public async Task Timeline_UpdateEvent_WithoutChanges_UsesDefaultDetails()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_UpdateEvent_WithoutChanges_UsesDefaultDetails));
        var fx = await SeedAsync(db);
        db.ActionLogs.Add(new ActionLog
        {
            ItemType = ItemType.Asset,
            ItemId = fx.Asset.Id,
            ActionType = ActionType.Update,
            CreatedBy = fx.Creator.Id,
            ActionDate = Base,
            Note = "cập nhật thiết bị"
        });
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50), CancellationToken.None);
        var update = result.Events!.Single(e => e.EventType == "UPDATE");

        Assert.Equal("Cập nhật thông tin thiết bị", update.Details);
    }

    /// <summary>[LL-4b] LogMeta hỏng/không đúng cấu trúc ⇒ KHÔNG ném, dùng details mặc định.</summary>
    [Fact]
    public async Task Timeline_UpdateEvent_WithUnparsableLogMeta_UsesDefaultDetails()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_UpdateEvent_WithUnparsableLogMeta_UsesDefaultDetails));
        var fx = await SeedAsync(db);
        db.ActionLogs.Add(new ActionLog
        {
            ItemType = ItemType.Asset,
            ItemId = fx.Asset.Id,
            ActionType = ActionType.Update,
            CreatedBy = fx.Creator.Id,
            ActionDate = Base,
            LogMeta = "{khong-phai-json"
        });
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50), CancellationToken.None);
        var update = result.Events!.Single(e => e.EventType == "UPDATE");

        Assert.Equal("Cập nhật thông tin thiết bị", update.Details);
    }

    /// <summary>[LL-4b] `id` sự kiện: duy nhất, ỔN ĐỊNH giữa 2 lần gọi, và phân biệt INSTALL/REMOVE của cùng một dòng.</summary>
    [Fact]
    public async Task Timeline_EventIds_AreStableUniqueAndDistinguishInstallFromRemove()
    {
        await using var db = TestHelpers.CreateContext(nameof(Timeline_EventIds_AreStableUniqueAndDistinguishInstallFromRemove));
        var fx = await SeedAllEventTypesAsync(db);

        var first = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50), CancellationToken.None);
        var second = await Handler(db).Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50), CancellationToken.None);

        var ids = first.Events!.Select(e => e.Id).ToList();
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(ids, second.Events!.Select(e => e.Id).ToList());

        var install = first.Events.Single(e => e.EventType == "INSTALL");
        var remove = first.Events.Single(e => e.EventType == "REMOVE");
        Assert.StartsWith("install:", install.Id);
        Assert.StartsWith("remove:", remove.Id);
        Assert.Equal(install.RefId, remove.RefId);   // cùng MỘT dòng lý lịch gốc...
        Assert.NotEqual(install.Id, remove.Id);      // ...nhưng id sự kiện phải khác nhau
    }
}
