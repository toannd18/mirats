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
        Assert.Equal("Lắp vào vị trí Vị trí 1", events[5].Title);
        Assert.Contains("Hệ thống: Hệ thống LL-3", events[5].Details);
        Assert.Equal("Người Lắp", events[5].ActorName);          // tên denormalize của lý lịch
        Assert.Equal("Tháo khỏi vị trí Vị trí 1", events[1].Title);

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
    }
}
