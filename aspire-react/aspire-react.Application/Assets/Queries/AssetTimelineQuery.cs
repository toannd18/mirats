using System.Globalization;
using System.Text;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Assets.Queries;

// ============================================================================
// [LL-3] Lý lịch thiết bị (asset timeline) — hợp đồng DTO cho FE (LL-4).
// Hợp đồng đầy đủ + từ vựng eventType: docs/API.md § "Asset Timeline".
// ============================================================================

/// <summary>
/// Từ vựng <c>eventType</c> cho các sự kiện KHÔNG đến từ ActionLog. Sự kiện đến từ ActionLog dùng
/// chính tên <see cref="ActionType"/> ở dạng SCREAMING_SNAKE (VD <c>CHECKOUT</c>, <c>UPDATE_REJECTED</c>).
/// </summary>
public static class AssetTimelineEventTypes
{
    /// <summary>Lắp thiết bị vào vị trí (từ lý lịch lắp đặt).</summary>
    public const string Install = "INSTALL";

    /// <summary>Tháo thiết bị khỏi vị trí (từ lý lịch lắp đặt — dòng đã đóng).</summary>
    public const string Remove = "REMOVE";

    /// <summary>Phiếu bảo dưỡng/sửa chữa của thiết bị (AssetMaintenance).</summary>
    public const string Maintenance = "MAINTENANCE";

    /// <summary>Thiết bị được đưa vào một đợt bảo dưỡng (MaintenanceCampaign qua DeviceSnapshot).</summary>
    public const string MaintenanceCampaign = "MAINTENANCE_CAMPAIGN";
}

/// <summary>
/// Giá trị <c>refType</c> — cho FE biết dùng <c>refId</c> để mở màn hình nào. KHÔNG phải FK cứng:
/// bản ghi có thể đã bị xóa cứng theo thời gian (FE phải chịu được 404 khi mở link).
/// </summary>
public static class AssetTimelineRefTypes
{
    /// <summary>Dòng lý lịch lắp đặt (<c>asset_installations</c>).</summary>
    public const string AssetInstallation = "assetInstallation";

    /// <summary>Bản ghi nhật ký thao tác (<c>action_logs</c>).</summary>
    public const string ActionLog = "actionLog";

    /// <summary>Phiếu bảo dưỡng (<c>asset_maintenances</c>).</summary>
    public const string AssetMaintenance = "assetMaintenance";

    /// <summary>Đợt bảo dưỡng (<c>maintenance_campaigns</c>).</summary>
    public const string MaintenanceCampaign = "maintenanceCampaign";
}

/// <summary>
/// Một sự kiện trong lý lịch thiết bị. Trả về theo thứ tự MỚI NHẤT TRƯỚC.
/// </summary>
/// <param name="EventType">Loại sự kiện (xem <see cref="AssetTimelineEventTypes"/> + tên ActionType SCREAMING_SNAKE).</param>
/// <param name="OccurredAt">Thời điểm sự kiện xảy ra (UTC) — KHÔNG phải thời điểm tạo bản ghi (trừ đợt bảo dưỡng, xem docs).</param>
/// <param name="Title">Tiêu đề hiển thị (tiếng Việt).</param>
/// <param name="Details">Chi tiết phụ (có thể null): hệ thống/địa điểm/đối tượng/ghi chú, ngăn cách bằng " · ".</param>
/// <param name="ActorName">Tên người thực hiện (null khi không xác định — VD dòng backfill).</param>
/// <param name="RefType">Loại bản ghi nguồn (xem <see cref="AssetTimelineRefTypes"/>; null nếu không có).</param>
/// <param name="RefId">Id bản ghi nguồn để FE mở link (null nếu không có).</param>
/// <param name="IsEstimated">True khi MỐC THỜI GIAN của sự kiện là ước tính (chỉ có thể xảy ra với sự kiện lắp của dòng backfill).</param>
public sealed record AssetTimelineEventDto(
    string EventType,
    DateTime OccurredAt,
    string Title,
    string? Details,
    string? ActorName,
    string? RefType,
    Guid? RefId,
    bool IsEstimated);

/// <summary>
/// Truy vấn lý lịch thiết bị. <paramref name="Cursor"/> là con trỏ đục (opaque) lấy từ
/// <c>pagination.nextCursor</c> của trang trước; <paramref name="Limit"/> mặc định 50, tối đa 200
/// (giá trị ngoài khoảng được CHUẨN HOÁ, không báo lỗi).
/// </summary>
public record AssetTimelineQuery(Guid AssetId, string? Cursor, int Limit = 50) : IRequest<AssetTimelineResult>
{
    /// <summary>Error code khi con trỏ phân trang không giải mã được → HTTP 400.</summary>
    public const string InvalidCursorErrorCode = "INVALID_CURSOR";
}

/// <summary>
/// Kết quả truy vấn lý lịch. <c>Success = false</c> + <c>ErrorCode = null</c> nghĩa là KHÔNG TÌM THẤY
/// (404 — asset không tồn tại hoặc ngoài phạm vi công ty của người dùng).
/// </summary>
public record AssetTimelineResult(
    bool Success,
    IReadOnlyList<AssetTimelineEventDto>? Events = null,
    string? NextCursor = null,
    int Limit = 0,
    string? Message = null,
    string? ErrorCode = null);

/// <summary>
/// Con trỏ phân trang của lý lịch thiết bị: <c>(OccurredAt, Id, Kind)</c> — KHÔNG dùng offset.
/// <para>
/// Thứ tự toàn phần của timeline là <c>(OccurredAt DESC, Id DESC, Kind DESC)</c>. Phải có <c>Kind</c>
/// vì một dòng lý lịch lắp đặt có thể sinh HAI sự kiện (lắp + tháo) trên cùng một dòng, và nếu
/// <c>StartedAt == EndedAt</c> (khoảng dài 0 — CHECK cho phép <c>&gt;=</c>) thì hai sự kiện trùng cả
/// <c>(OccurredAt, Id)</c>; <c>Kind</c> phá thế bằng nhau đó để phân trang KHÔNG mất/KHÔNG lặp.
/// </para>
/// <para>
/// Mã hoá base64url của <c>ticks|guid|kind</c> (thời gian giữ nguyên đến 100ns nên round-trip qua
/// <c>timestamptz</c> là chính xác). Con trỏ là ĐỤC với client — không có gì bảo đảm về nội dung.
/// </para>
/// </summary>
public readonly record struct AssetTimelineCursor(DateTime OccurredAt, Guid Id, int Kind)
{
    /// <summary>Mã hoá con trỏ (base64url, không padding).</summary>
    public string Encode()
    {
        var raw = string.Create(CultureInfo.InvariantCulture, $"{OccurredAt.Ticks}|{Id:N}|{Kind}");
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <summary>Giải mã con trỏ; trả false (không ném) với mọi đầu vào không hợp lệ.</summary>
    public static bool TryDecode(string? value, out AssetTimelineCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        try
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            normalized = (normalized.Length % 4) switch
            {
                2 => normalized + "==",
                3 => normalized + "=",
                _ => normalized
            };

            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
            var parts = raw.Split('|');
            if (parts.Length != 3)
                return false;

            if (!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
                return false;
            if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
                return false;
            if (!Guid.TryParseExact(parts[1], "N", out var id))
                return false;
            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var kind))
                return false;

            cursor = new AssetTimelineCursor(new DateTime(ticks, DateTimeKind.Utc), id, kind);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}

/// <summary>
/// Nguồn của một sự kiện + thứ tự phá thế bằng nhau (Kind) trong con trỏ. Giá trị số là HỢP ĐỒNG
/// nội bộ: chỉ được THÊM, không đổi giá trị đã phát hành (con trỏ cũ vẫn phải giải mã đúng).
/// </summary>
internal enum TimelineSourceKind
{
    LedgerInstall = 1,
    LedgerRemove = 2,
    ActionLog = 3,
    CampaignSnapshot = 4,
    Maintenance = 5
}

/// <summary>
/// [LL-3] Lý lịch thiết bị — trộn 4 nguồn (KHÔNG dùng Assignment: trùng ActionLog):
/// <list type="number">
///   <item><c>asset_installations</c> → sự kiện lắp (<c>INSTALL</c>) và, nếu dòng đã đóng, sự kiện tháo
///     (<c>REMOVE</c>). BỎ dòng đã bị đính chính (<c>SupersededAt != null</c>).</item>
///   <item><c>action_logs</c> (ItemType = Asset, ItemId = asset, chưa soft-delete) → mọi sự kiện còn lại.
///     <b>Điều kiện khử trùng</b>: BỎ <c>Checkout</c>/<c>Checkin</c> có <c>TargetType = SystemPosition</c>
///     vì các thao tác đó đã được biểu diễn bằng sự kiện lắp/tháo ở nguồn 1 (nếu không bỏ sẽ lặp).</item>
///   <item><c>maintenance_campaign_device_snapshots</c> (theo AssetId) → thiết bị được đưa vào đợt bảo dưỡng
///     (<c>MAINTENANCE_CAMPAIGN</c>); mốc thời gian = ngày bắt đầu đợt (<c>MaintenanceCampaign.StartDate</c>).</item>
///   <item><c>asset_maintenances</c> (theo AssetId, chưa soft-delete) → phiếu bảo dưỡng/sửa chữa
///     (<c>MAINTENANCE</c>); mốc thời gian = <c>StartDate</c> của phiếu.</item>
/// </list>
/// Phân trang theo con trỏ <see cref="AssetTimelineCursor"/> (không offset); mỗi nguồn tự lọc + sắp xếp +
/// lấy tối đa <c>limit + 1</c> rồi trộn trong bộ nhớ — vì thứ tự cục bộ của từng nguồn là hạn chế của
/// thứ tự toàn phần <c>(OccurredAt, Id, Kind)</c>, nên cách trộn này KHÔNG mất/KHÔNG lặp sự kiện.
/// </summary>
public class AssetTimelineQueryHandler : IRequestHandler<AssetTimelineQuery, AssetTimelineResult>
{
    /// <summary>Số sự kiện mỗi trang khi client không truyền <c>limit</c>.</summary>
    public const int DefaultLimit = 50;

    /// <summary>Trần số sự kiện mỗi trang (giá trị lớn hơn bị chuẩn hoá về đây).</summary>
    public const int MaxLimit = 200;

    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public AssetTimelineQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    /// <summary>Chuẩn hoá limit: ≤ 0 → <see cref="DefaultLimit"/>; &gt; <see cref="MaxLimit"/> → <see cref="MaxLimit"/>.</summary>
    public static int NormalizeLimit(int limit) =>
        limit <= 0 ? DefaultLimit : Math.Min(limit, MaxLimit);

    public async Task<AssetTimelineResult> Handle(AssetTimelineQuery request, CancellationToken cancellationToken)
    {
        var limit = NormalizeLimit(request.Limit);
        var take = limit + 1;

        AssetTimelineCursor? cursor = null;
        if (!string.IsNullOrWhiteSpace(request.Cursor))
        {
            if (!AssetTimelineCursor.TryDecode(request.Cursor, out var parsed))
            {
                return new AssetTimelineResult(
                    false, Limit: limit,
                    Message: "Con trỏ phân trang không hợp lệ.",
                    ErrorCode: AssetTimelineQuery.InvalidCursorErrorCode);
            }

            cursor = parsed;
        }

        // Company scoping — MIRROR GetAssetByIdQueryHandler: asset ngoài phạm vi công ty coi như không tồn tại.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var asset = await _context.Assets.AsNoTracking()
            .Where(a => a.Id == request.AssetId)
            .Select(a => new { a.Id, a.CompanyId })
            .FirstOrDefaultAsync(cancellationToken);

        if (asset == null
            || (userCompanyId.HasValue && asset.CompanyId.HasValue && asset.CompanyId.Value != userCompanyId.Value))
        {
            return new AssetTimelineResult(false, Limit: limit);
        }

        var assetId = asset.Id;
        var candidates = new List<TimelineCandidate>(take * 5);
        candidates.AddRange(await FetchLedgerInstallAsync(assetId, cursor, take, cancellationToken));
        candidates.AddRange(await FetchLedgerRemoveAsync(assetId, cursor, take, cancellationToken));
        candidates.AddRange(await FetchActionLogsAsync(assetId, cursor, take, cancellationToken));
        candidates.AddRange(await FetchCampaignSnapshotsAsync(assetId, cursor, take, cancellationToken));
        candidates.AddRange(await FetchMaintenancesAsync(assetId, cursor, take, cancellationToken));

        var ordered = candidates
            .OrderByDescending(c => c.OccurredAt)
            .ThenByDescending(c => c.Id)
            .ThenByDescending(c => (int)c.Source)
            .ToList();

        var hasMore = ordered.Count > limit;
        var page = ordered.Take(limit).ToList();
        var nextCursor = hasMore && page.Count > 0
            ? new AssetTimelineCursor(page[^1].OccurredAt, page[^1].Id, (int)page[^1].Source).Encode()
            : null;

        var lookups = await BuildLookupsAsync(page, cancellationToken);
        var events = page.Select(c => Compose(c, lookups)).ToList();

        return new AssetTimelineResult(true, events, nextCursor, limit);
    }

    // ==================== NGUỒN 1+2: lý lịch lắp đặt ====================

    private async Task<List<TimelineCandidate>> FetchLedgerInstallAsync(
        Guid assetId, AssetTimelineCursor? cursor, int take, CancellationToken cancellationToken)
    {
        const TimelineSourceKind kind = TimelineSourceKind.LedgerInstall;

        var query = _context.AssetInstallations.AsNoTracking()
            .Where(i => i.AssetId == assetId && i.SupersededAt == null);

        if (cursor is { } c)
        {
            var at = c.OccurredAt;
            var id = c.Id;
            query = (int)kind < c.Kind
                ? query.Where(i => i.StartedAt < at || (i.StartedAt == at && i.Id.CompareTo(id) <= 0))
                : query.Where(i => i.StartedAt < at || (i.StartedAt == at && i.Id.CompareTo(id) < 0));
        }

        var rows = await query
            .OrderByDescending(i => i.StartedAt).ThenByDescending(i => i.Id)
            .Take(take)
            .Select(i => new
            {
                i.Id,
                i.StartedAt,
                i.SystemPositionName,
                i.SystemInfoName,
                i.Note,
                i.LoggedByName,
                i.LoggedById,
                i.Source
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new TimelineCandidate(
            kind,
            r.StartedAt,
            r.Id,
            AssetTimelineRefTypes.AssetInstallation,
            r.Id,
            r.LoggedByName,
            r.LoggedById,
            PositionName: r.SystemPositionName,
            SystemInfoName: r.SystemInfoName,
            IsEstimated: r.Source == AssetInstallationSource.Backfill,
            Note: r.Note)).ToList();
    }

    private async Task<List<TimelineCandidate>> FetchLedgerRemoveAsync(
        Guid assetId, AssetTimelineCursor? cursor, int take, CancellationToken cancellationToken)
    {
        const TimelineSourceKind kind = TimelineSourceKind.LedgerRemove;

        var query = _context.AssetInstallations.AsNoTracking()
            .Where(i => i.AssetId == assetId && i.SupersededAt == null && i.EndedAt != null);

        if (cursor is { } c)
        {
            var at = c.OccurredAt;
            var id = c.Id;
            query = (int)kind < c.Kind
                ? query.Where(i => i.EndedAt < at || (i.EndedAt == at && i.Id.CompareTo(id) <= 0))
                : query.Where(i => i.EndedAt < at || (i.EndedAt == at && i.Id.CompareTo(id) < 0));
        }

        var rows = await query
            .OrderByDescending(i => i.EndedAt).ThenByDescending(i => i.Id)
            .Take(take)
            .Select(i => new
            {
                i.Id,
                i.EndedAt,
                i.SystemPositionName,
                i.SystemInfoName,
                i.Note,
                i.LoggedByName,
                i.LoggedById
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new TimelineCandidate(
            kind,
            r.EndedAt!.Value,
            r.Id,
            AssetTimelineRefTypes.AssetInstallation,
            r.Id,
            r.LoggedByName,
            r.LoggedById,
            PositionName: r.SystemPositionName,
            SystemInfoName: r.SystemInfoName,
            IsEstimated: false, // mốc tháo luôn đến từ thao tác thật (backfill chỉ ước tính StartedAt)
            Note: r.Note)).ToList();
    }

    // ==================== NGUỒN 3: ActionLog ====================

    private async Task<List<TimelineCandidate>> FetchActionLogsAsync(
        Guid assetId, AssetTimelineCursor? cursor, int take, CancellationToken cancellationToken)
    {
        const TimelineSourceKind kind = TimelineSourceKind.ActionLog;

        var query = _context.ActionLogs.AsNoTracking()
            .Where(l => l.ItemType == ItemType.Asset
                        && l.ItemId == assetId
                        && l.DeletedAt == null
                        // KHỬ TRÙNG: checkout/checkin tới SystemPosition đã có sự kiện lắp/tháo (nguồn 1).
                        && !(l.TargetType == AssignmentTargetType.SystemPosition
                             && (l.ActionType == ActionType.Checkout || l.ActionType == ActionType.Checkin)));

        if (cursor is { } c)
        {
            var at = c.OccurredAt;
            var id = c.Id;
            query = (int)kind < c.Kind
                ? query.Where(l => l.ActionDate < at || (l.ActionDate == at && l.Id.CompareTo(id) <= 0))
                : query.Where(l => l.ActionDate < at || (l.ActionDate == at && l.Id.CompareTo(id) < 0));
        }

        var rows = await query
            .OrderByDescending(l => l.ActionDate).ThenByDescending(l => l.Id)
            .Take(take)
            .Select(l => new
            {
                l.Id,
                l.ActionDate,
                l.ActionType,
                l.TargetType,
                l.TargetId,
                l.Note,
                l.LocationName,
                l.CreatedBy
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new TimelineCandidate(
            kind,
            r.ActionDate,
            r.Id,
            AssetTimelineRefTypes.ActionLog,
            r.Id,
            ActorName: null,
            ActorUserId: r.CreatedBy,
            ActionType: r.ActionType,
            TargetType: r.TargetType,
            TargetId: r.TargetId,
            LocationName: r.LocationName,
            Note: r.Note)).ToList();
    }

    // ==================== NGUỒN 4: đợt bảo dưỡng (qua DeviceSnapshot) ====================

    private async Task<List<TimelineCandidate>> FetchCampaignSnapshotsAsync(
        Guid assetId, AssetTimelineCursor? cursor, int take, CancellationToken cancellationToken)
    {
        const TimelineSourceKind kind = TimelineSourceKind.CampaignSnapshot;

        // Mốc thời gian = NGÀY BẮT ĐẦU ĐỢT (MaintenanceCampaign.StartDate — mốc nghiệp vụ do người dùng đặt),
        // KHÔNG dùng snapshot.CreatedAt vì cột đó do tầng audit đóng dấu lúc ghi bản ghi.
        var query = _context.MaintenanceCampaignDeviceSnapshots.AsNoTracking()
            .Where(s => s.AssetId == assetId);

        if (cursor is { } c)
        {
            var at = c.OccurredAt;
            var id = c.Id;
            query = (int)kind < c.Kind
                ? query.Where(s => s.Campaign.StartDate < at || (s.Campaign.StartDate == at && s.Id.CompareTo(id) <= 0))
                : query.Where(s => s.Campaign.StartDate < at || (s.Campaign.StartDate == at && s.Id.CompareTo(id) < 0));
        }

        var rows = await query
            .OrderByDescending(s => s.Campaign.StartDate).ThenByDescending(s => s.Id)
            .Take(take)
            .Select(s => new
            {
                s.Id,
                OccurredAt = s.Campaign.StartDate,
                s.CampaignId,
                s.SystemPositionName,
                ReviewerId = s.Campaign.ReviewerId
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new TimelineCandidate(
            kind,
            r.OccurredAt,
            r.Id,
            AssetTimelineRefTypes.MaintenanceCampaign,
            r.CampaignId,
            ActorName: null,
            // Người phụ trách đợt — PHẢI khai báo ở đây để lọt vào lô tra cứu tên; nếu để null thì sự kiện
            // đợt bảo dưỡng chỉ có tên khi TÌNH CỜ có sự kiện khác trong trang cùng người (đã gặp thật:
            // limit=1 → actorName null, limit=3 → có tên).
            ActorUserId: r.ReviewerId,
            PositionName: r.SystemPositionName,
            CampaignId: r.CampaignId)).ToList();
    }

    // ==================== NGUỒN 5: phiếu bảo dưỡng ====================

    private async Task<List<TimelineCandidate>> FetchMaintenancesAsync(
        Guid assetId, AssetTimelineCursor? cursor, int take, CancellationToken cancellationToken)
    {
        const TimelineSourceKind kind = TimelineSourceKind.Maintenance;

        var query = _context.AssetMaintenances.AsNoTracking()
            .Where(m => m.AssetId == assetId && m.DeletedAt == null);

        if (cursor is { } c)
        {
            // Cột này là `timestamp without time zone` ⇒ PHẢI hạ Kind xuống Unspecified (xem ToColumnKind).
            var at = ToColumnKind(c.OccurredAt);
            var id = c.Id;
            query = (int)kind < c.Kind
                ? query.Where(m => m.StartDate < at || (m.StartDate == at && m.Id.CompareTo(id) <= 0))
                : query.Where(m => m.StartDate < at || (m.StartDate == at && m.Id.CompareTo(id) < 0));
        }

        var rows = await query
            .OrderByDescending(m => m.StartDate).ThenByDescending(m => m.Id)
            .Take(take)
            .Select(m => new
            {
                m.Id,
                m.StartDate,
                m.Type,
                m.Title,
                m.CompletionDate,
                m.CreatedById
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new TimelineCandidate(
            kind,
            ToApiKind(r.StartDate),
            r.Id,
            AssetTimelineRefTypes.AssetMaintenance,
            r.Id,
            ActorName: null,
            ActorUserId: r.CreatedById,
            MaintenanceType: r.Type,
            MaintenanceTitle: r.Title,
            CompletionDate: r.CompletionDate)).ToList();
    }

    // ==================== Tra cứu tên (chỉ cho TRANG đã chọn — tránh N+1) ====================

    private async Task<TimelineLookups> BuildLookupsAsync(
        IReadOnlyList<TimelineCandidate> page, CancellationToken cancellationToken)
    {
        var userIds = page
            .Select(c => c.ActorUserId)
            .Concat(page
                .Where(c => c.TargetType == AssignmentTargetType.User)
                .Select(c => c.TargetId))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var departmentIds = page
            .Where(c => c.TargetType == AssignmentTargetType.Department)
            .Select(c => c.TargetId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var positionIds = page
            .Where(c => c.TargetType == AssignmentTargetType.SystemPosition)
            .Select(c => c.TargetId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var campaignIds = page
            .Where(c => c.CampaignId.HasValue)
            .Select(c => c.CampaignId!.Value)
            .Distinct()
            .ToList();

        var userNames = new Dictionary<Guid, string>();
        if (userIds.Count > 0)
        {
            var users = await _context.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Username, u.FirstName, u.LastName })
                .ToListAsync(cancellationToken);
            foreach (var u in users)
                userNames[u.Id] = FormatPersonName(u.FirstName, u.LastName, u.Username);
        }

        var departmentNames = new Dictionary<Guid, string>();
        if (departmentIds.Count > 0)
        {
            var departments = await _context.Departments.AsNoTracking()
                .Where(d => departmentIds.Contains(d.Id))
                .Select(d => new { d.Id, d.Name })
                .ToListAsync(cancellationToken);
            foreach (var d in departments)
                departmentNames[d.Id] = d.Name;
        }

        var positionNames = new Dictionary<Guid, string>();
        if (positionIds.Count > 0)
        {
            var positions = await _context.SystemPositions.AsNoTracking()
                .Where(p => positionIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Name })
                .ToListAsync(cancellationToken);
            foreach (var p in positions)
                positionNames[p.Id] = p.Name;
        }

        var campaigns = new Dictionary<Guid, TimelineCampaignInfo>();
        if (campaignIds.Count > 0)
        {
            var rows = await (from c in _context.MaintenanceCampaigns.AsNoTracking()
                              join sys in _context.SystemInfos.AsNoTracking() on c.SystemInfoId equals sys.Id
                              where campaignIds.Contains(c.Id)
                              select new
                              {
                                  c.Id,
                                  c.BatchNumber,
                                  c.StartDate,
                                  c.Status,
                                  c.ReviewerId,
                                  SystemName = (string?)sys.Name
                              }).ToListAsync(cancellationToken);

            foreach (var c in rows)
                campaigns[c.Id] = new TimelineCampaignInfo(c.BatchNumber, c.StartDate, c.Status, c.SystemName, c.ReviewerId);
        }

        return new TimelineLookups(userNames, departmentNames, positionNames, campaigns);
    }

    // ==================== Ghép DTO ====================

    private static AssetTimelineEventDto Compose(TimelineCandidate c, TimelineLookups lookups)
    {
        switch (c.Source)
        {
            case TimelineSourceKind.LedgerInstall:
                return new AssetTimelineEventDto(
                    AssetTimelineEventTypes.Install,
                    c.OccurredAt,
                    $"Lắp vào vị trí {DisplayPosition(c.PositionName)}",
                    JoinDetails(
                        c.SystemInfoName == null ? null : $"Hệ thống: {c.SystemInfoName}",
                        c.Note),
                    ResolveActorName(c.ActorUserId, c.ActorName, lookups),
                    c.RefType,
                    c.RefId,
                    c.IsEstimated);

            case TimelineSourceKind.LedgerRemove:
                return new AssetTimelineEventDto(
                    AssetTimelineEventTypes.Remove,
                    c.OccurredAt,
                    $"Tháo khỏi vị trí {DisplayPosition(c.PositionName)}",
                    JoinDetails(
                        c.SystemInfoName == null ? null : $"Hệ thống: {c.SystemInfoName}",
                        c.Note),
                    ResolveActorName(c.ActorUserId, c.ActorName, lookups),
                    c.RefType,
                    c.RefId,
                    c.IsEstimated);

            case TimelineSourceKind.ActionLog:
                {
                    var actionType = c.ActionType ?? ActionType.Update;
                    var (eventType, title) = DescribeActionType(actionType);

                    string? target = null;
                    if (c.TargetType == AssignmentTargetType.User && c.TargetId.HasValue
                        && lookups.UserNames.TryGetValue(c.TargetId.Value, out var userName))
                        target = $"cho {userName}";
                    else if (c.TargetType == AssignmentTargetType.Department && c.TargetId.HasValue
                             && lookups.DepartmentNames.TryGetValue(c.TargetId.Value, out var departmentName))
                        target = $"cho bộ phận {departmentName}";
                    else if (c.TargetType == AssignmentTargetType.SystemPosition && c.TargetId.HasValue
                             && lookups.PositionNames.TryGetValue(c.TargetId.Value, out var positionName))
                        target = $"tại vị trí {positionName}";

                    return new AssetTimelineEventDto(
                        eventType,
                        c.OccurredAt,
                        title,
                        JoinDetails(
                            target,
                            c.LocationName == null ? null : $"Địa điểm: {c.LocationName}",
                            c.Note),
                        ResolveActorName(c.ActorUserId, c.ActorName, lookups),
                        c.RefType,
                        c.RefId,
                        false);
                }

            case TimelineSourceKind.CampaignSnapshot:
                {
                    lookups.Campaigns.TryGetValue(c.CampaignId ?? Guid.Empty, out var campaign);
                    var batch = string.IsNullOrWhiteSpace(campaign?.BatchNumber) ? "(không số đợt)" : campaign!.BatchNumber!;

                    return new AssetTimelineEventDto(
                        AssetTimelineEventTypes.MaintenanceCampaign,
                        c.OccurredAt,
                        $"Đưa vào đợt bảo dưỡng {batch}",
                        JoinDetails(
                            campaign?.SystemName == null ? null : $"Hệ thống: {campaign.SystemName}",
                            campaign == null ? null : $"Bắt đầu đợt: {campaign.StartDate:dd/MM/yyyy}",
                            campaign == null ? null : $"Trạng thái đợt: {DescribeCampaignStatus(campaign.Status)}",
                            c.PositionName == null ? null : $"Vị trí trong đợt: {c.PositionName}"),
                        // Người phụ trách đợt: lấy từ candidate (ActorUserId = Campaign.ReviewerId) để tên được
                        // tra trong lô của TRANG hiện tại — không phụ thuộc việc campaign còn tra cứu được hay không.
                        ResolveActorName(c.ActorUserId, null, lookups),
                        c.RefType,
                        c.RefId,
                        false);
                }

            case TimelineSourceKind.Maintenance:
                {
                    var typeLabel = DescribeMaintenanceType(c.MaintenanceType ?? AssetMaintenanceType.Maintenance);
                    var title = string.IsNullOrWhiteSpace(c.MaintenanceTitle) ? typeLabel : $"{typeLabel}: {c.MaintenanceTitle}";

                    return new AssetTimelineEventDto(
                        AssetTimelineEventTypes.Maintenance,
                        c.OccurredAt,
                        title,
                        JoinDetails(
                            $"Bắt đầu: {c.OccurredAt:dd/MM/yyyy}",
                            c.CompletionDate.HasValue
                                ? $"Hoàn thành: {c.CompletionDate.Value:dd/MM/yyyy}"
                                : "Chưa hoàn thành"),
                        ResolveActorName(c.ActorUserId, c.ActorName, lookups),
                        c.RefType,
                        c.RefId,
                        false);
                }

            default:
                return new AssetTimelineEventDto(
                    AssetTimelineEventTypes.Maintenance, c.OccurredAt, "Sự kiện", null,
                    ResolveActorName(c.ActorUserId, c.ActorName, lookups), c.RefType, c.RefId, c.IsEstimated);
        }
    }

    private static string DisplayPosition(string? positionName) =>
        string.IsNullOrWhiteSpace(positionName) ? "(vị trí đã xóa)" : positionName;

    private static string? ResolveActorName(Guid? userId, string? denormalizedName, TimelineLookups lookups)
    {
        if (!string.IsNullOrWhiteSpace(denormalizedName))
            return denormalizedName;
        if (userId.HasValue && lookups.UserNames.TryGetValue(userId.Value, out var name))
            return name;
        return null;
    }

    private static string FormatPersonName(string? firstName, string? lastName, string? username)
    {
        var fullName = $"{firstName} {lastName}".Trim();
        return string.IsNullOrWhiteSpace(fullName) ? (username ?? string.Empty) : fullName;
    }

    private static string? JoinDetails(params string?[] parts)
    {
        var kept = parts
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .ToArray();
        return kept.Length == 0 ? null : string.Join(" · ", kept);
    }

    /// <summary>
    /// Nhãn tiếng Việt + eventType cho một <see cref="ActionType"/> (khớp nhãn đang dùng ở FE
    /// <c>ActionLogTable.ACTION_TYPE_TAGS</c> để 2 màn hình không lệch nhau).
    /// </summary>
    private static (string EventType, string Title) DescribeActionType(ActionType actionType) => actionType switch
    {
        ActionType.Create => ("CREATE", "Tạo mới"),
        ActionType.Update => ("UPDATE", "Cập nhật"),
        ActionType.Delete => ("DELETE", "Xóa"),
        ActionType.Checkout => ("CHECKOUT", "Cấp phát"),
        ActionType.Checkin => ("CHECKIN", "Thu hồi"),
        ActionType.Audit => ("AUDIT", "Kiểm kê"),
        ActionType.Import => ("IMPORT", "Import"),
        ActionType.Export => ("EXPORT", "Export"),
        ActionType.Accept => ("ACCEPT", "Chấp nhận"),
        ActionType.Decline => ("DECLINE", "Từ chối"),
        ActionType.Confirm => ("CONFIRM", "Xác nhận"),
        ActionType.Archive => ("ARCHIVE", "Lưu trữ"),
        ActionType.Unarchive => ("UNARCHIVE", "Mở lại"),
        ActionType.UpdateRejected => ("UPDATE_REJECTED", "Cập nhật bị từ chối"),
        ActionType.StockIn => ("STOCK_IN", "Nhập kho"),
        ActionType.MarkDamaged => ("MARK_DAMAGED", "Đánh dấu hỏng"),
        ActionType.Dispose => ("DISPOSE", "Loại bỏ"),
        ActionType.Close => ("CLOSE", "Đóng"),
        ActionType.Reopen => ("REOPEN", "Mở lại"),
        ActionType.Inspect => ("INSPECT", "Kiểm tra"),
        ActionType.Publish => ("PUBLISH", "Phát hành"),
        ActionType.Complete => ("COMPLETE", "Hoàn thành"),
        _ => (ToScreamingSnake(actionType.ToString()), actionType.ToString())
    };

    private static string DescribeCampaignStatus(MaintenanceCampaignStatus status) => status switch
    {
        MaintenanceCampaignStatus.InProgress => "Đang thực hiện",
        MaintenanceCampaignStatus.Completed => "Đã hoàn thành",
        _ => status.ToString()
    };

    private static string DescribeMaintenanceType(AssetMaintenanceType type) => type switch
    {
        AssetMaintenanceType.Maintenance => "Bảo trì định kỳ",
        AssetMaintenanceType.Repair => "Sửa chữa",
        AssetMaintenanceType.Upgrade => "Nâng cấp",
        AssetMaintenanceType.HardwareSupport => "Hỗ trợ phần cứng",
        AssetMaintenanceType.SoftwareSupport => "Hỗ trợ phần mềm",
        AssetMaintenanceType.PatTest => "Kiểm tra an toàn điện (PAT)",
        AssetMaintenanceType.Calibration => "Hiệu chuẩn",
        AssetMaintenanceType.IncidentReport => "Báo cáo sự cố",
        _ => type.ToString()
    };

    /// <summary>
    /// [LL-3][DateTime Kind] Cột <c>asset_maintenances.StartDate</c> là <c>timestamp without time zone</c> ⇒
    /// tham số so sánh truyền xuống SQL PHẢI là <see cref="DateTimeKind.Unspecified"/>. Nếu không, Npgsql ném
    /// <c>ArgumentException: Cannot write DateTime with Kind=UTC to PostgreSQL type 'timestamp without time zone'</c>
    /// → **500** (đã gặp THẬT trên Postgres khi lật sang trang 2 — InMemory không phát hiện được).
    /// <c>public</c> (không phải private) để test suite giữ được canary cho quy tắc Kind này.
    /// </summary>
    public static DateTime ToColumnKind(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

    /// <summary>
    /// [LL-3] Giá trị ĐỌC từ cột <c>timestamp without time zone</c> là wall-clock UTC (mọi luồng ghi của app dùng
    /// <c>UtcNow</c>/<c>toISOString()</c> rồi <c>SpecifyKind(..., Unspecified)</c>) ⇒ chuẩn hoá Kind=UTC để MỌI
    /// <c>occurredAt</c> trong response là cùng một quy ước (có hậu tố <c>Z</c>) — nếu trả về Kind=Unspecified thì
    /// JSON mất hậu tố và client sẽ hiểu sai múi giờ khi trộn 4 nguồn.
    /// </summary>
    public static DateTime ToApiKind(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    /// <summary>PascalCase → SCREAMING_SNAKE (fallback cho ActionType mới chưa có nhãn).</summary>
    private static string ToScreamingSnake(string value)
    {
        var builder = new StringBuilder(value.Length + 4);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsUpper(ch) && i > 0)
                builder.Append('_');
            builder.Append(char.ToUpperInvariant(ch));
        }

        return builder.ToString();
    }

    // ==================== Kiểu nội bộ ====================

    /// <summary>Một sự kiện CHƯA ghép DTO (đủ dữ liệu cho cả 5 nguồn + sắp xếp/con trỏ).</summary>
    private sealed record TimelineCandidate(
        TimelineSourceKind Source,
        DateTime OccurredAt,
        Guid Id,
        string RefType,
        Guid? RefId,
        string? ActorName,
        Guid? ActorUserId,
        string? PositionName = null,
        string? SystemInfoName = null,
        bool IsEstimated = false,
        string? Note = null,
        ActionType? ActionType = null,
        AssignmentTargetType? TargetType = null,
        Guid? TargetId = null,
        string? LocationName = null,
        Guid? CampaignId = null,
        AssetMaintenanceType? MaintenanceType = null,
        string? MaintenanceTitle = null,
        DateTime? CompletionDate = null);

    /// <summary>Thông tin đợt bảo dưỡng tra cứu theo lô cho TRANG hiện tại.</summary>
    private sealed record TimelineCampaignInfo(
        string? BatchNumber,
        DateTime StartDate,
        MaintenanceCampaignStatus Status,
        string? SystemName,
        Guid? ReviewerId);

    /// <summary>Tên tra cứu theo lô (chỉ cho trang đã chọn) — tránh N+1 và tránh INNER JOIN làm mất dòng.</summary>
    private sealed record TimelineLookups(
        IReadOnlyDictionary<Guid, string> UserNames,
        IReadOnlyDictionary<Guid, string> DepartmentNames,
        IReadOnlyDictionary<Guid, string> PositionNames,
        IReadOnlyDictionary<Guid, TimelineCampaignInfo> Campaigns);
}
