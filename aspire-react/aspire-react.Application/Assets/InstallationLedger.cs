using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Assets;

/// <summary>
/// [LL-2] Ghi lý lịch lắp đặt thiết bị (bảng <c>asset_installations</c>, chỉ ghi thêm) từ các thao tác
/// nghiệp vụ trên Asset. Mọi hàm ở đây chỉ sửa CHANGE TRACKER — caller gọi <c>SaveChangesAsync</c>
/// trong CÙNG transaction với asset/assignment/ActionLog (convention LL-1/LL-2).
/// <para>
/// Bất biến trung tâm: <c>Asset.SystemPositionId != null</c> ⟺ ĐÚNG MỘT dòng "đang mở"
/// (<c>EndedAt IS NULL AND SupersededAt IS NULL</c>), và dòng đó trỏ đúng vị trí hiện tại.
/// Caller kiểm tra bằng <see cref="IsConsistentAsync"/> SAU SaveChanges và TRƯỚC Commit:
/// sai → rollback + <see cref="Inconsistent"/>.
/// </para>
/// </summary>
public static class InstallationLedger
{
    /// <summary>Bất biến "SystemPositionId ⟺ đúng 1 dòng mở" bị phá → 400 (rollback, không ghi gì).</summary>
    public const string Inconsistent = "INSTALLATION_LEDGER_INCONSISTENT";

    /// <summary>Exclusion constraint 23P01 (hai khoảng lắp đặt chồng nhau) → 409 (không lọt 500).</summary>
    public const string Overlap = "INSTALLATION_PERIOD_OVERLAP";

    /// <summary>
    /// [LL-2b] Mốc kết thúc (checkin/archive) TRƯỚC mốc bắt đầu của khoảng đang mở → 400.
    /// Chặn trước SaveChanges; SQLSTATE 23514 (CHECK <c>EndedAt &gt;= StartedAt</c>) là lưới an toàn.
    /// </summary>
    public const string EndBeforeStart = "INSTALLATION_END_BEFORE_START";

    /// <summary>
    /// Kiểm tra TRƯỚC khi thay đổi (pre-check) — KHÔNG sửa dữ liệu để che lệch:
    /// <list type="number">
    ///   <item>Bất biến của trạng thái HIỆN TẠI: <c>SystemPositionId != null</c> ⟺ đúng 1 dòng mở
    ///     (chưa superseded) trỏ đúng vị trí → <see cref="Inconsistent"/>.</item>
    ///   <item>Nếu lệnh sắp ĐÓNG khoảng (<paramref name="closingAt"/> != null): mốc kết thúc không được
    ///     trước <c>StartedAt</c> của dòng đang mở → <see cref="EndBeforeStart"/>.</item>
    /// </list>
    /// Trả về error code (null = được phép thay đổi). Gọi SAU khi đã khoá hàng asset (FOR UPDATE) và
    /// TRƯỚC mọi thay đổi change tracker.
    /// </summary>
    public static async Task<string?> ValidateBeforeChangeAsync(
        IApplicationDbContext context,
        Asset asset,
        DateTime? closingAt,
        CancellationToken cancellationToken)
    {
        if (!await IsConsistentAsync(context, asset, cancellationToken))
            return Inconsistent;

        if (closingAt.HasValue)
        {
            var openStartedAt = await context.AssetInstallations.AsNoTracking()
                .Where(i => i.AssetId == asset.Id && i.EndedAt == null && i.SupersededAt == null)
                .Select(i => (DateTime?)i.StartedAt)
                .OrderBy(d => d)
                .FirstOrDefaultAsync(cancellationToken);

            if (openStartedAt.HasValue && closingAt.Value < openStartedAt.Value)
                return EndBeforeStart;
        }

        return null;
    }

    /// <summary>Message hiển thị cho người dùng theo error code pre-check của sổ lý lịch (LL-2b).</summary>
    public static string DescribeError(string errorCode) => errorCode switch
    {
        Inconsistent => "Lý lịch lắp đặt của thiết bị không nhất quán với trạng thái hiện tại — thao tác đã được hoàn tác.",
        EndBeforeStart => "Mốc thời gian kết thúc trước mốc bắt đầu lắp đặt — kiểm tra lại thời điểm tháo/lưu trữ.",
        _ => "Thao tác không thực hiện được do lý lịch lắp đặt của thiết bị."
    };

    /// <summary>
    /// Mở một khoảng lắp đặt MỚI (append-only) cho asset tại <paramref name="position"/>; KHÔNG tự
    /// SaveChanges. Denormalize tên vị trí + tên hệ thống để lịch sử sống sót khi vị trí/hệ thống bị
    /// đổi tên hoặc xóa (FK SystemPositionId là SET NULL).
    /// </summary>
    public static AssetInstallation Open(
        IApplicationDbContext context,
        Guid assetId,
        SystemPosition position,
        string? systemInfoName,
        DateTime startedAt,
        Guid? loggedById,
        string? loggedByName,
        AssetInstallationSource source,
        string? note = null)
    {
        var row = new AssetInstallation
        {
            AssetId = assetId,
            SystemPositionId = position.Id,
            SystemPositionName = position.Name,
            SystemInfoId = position.SystemInfoId,
            SystemInfoName = systemInfoName,
            StartedAt = startedAt,
            LoggedById = loggedById,
            LoggedByName = loggedByName,
            Source = source,
            Note = note
        };

        context.AssetInstallations.Add(row);
        return row;
    }

    /// <summary>
    /// Đóng dòng ĐANG MỞ của asset (<c>EndedAt IS NULL AND SupersededAt IS NULL</c>) tại
    /// <paramref name="endedAt"/> — cùng mốc thời gian với checkin/archive. Trả về số dòng đã đóng.
    /// Bảng chỉ ghi thêm: <c>EndedAt</c> chỉ được đặt khi đang null (đúng 1 lần), không bao giờ ghi đè.
    /// </summary>
    public static async Task<int> CloseOpenAsync(
        IApplicationDbContext context,
        Guid assetId,
        DateTime endedAt,
        CancellationToken cancellationToken)
    {
        var openRows = await context.AssetInstallations
            .Where(i => i.AssetId == assetId && i.EndedAt == null && i.SupersededAt == null)
            .ToListAsync(cancellationToken);

        var closed = 0;
        foreach (var row in openRows)
        {
            if (row.EndedAt != null) continue; // bất biến: đúng 1 chuyển trạng thái null → giá trị
            row.EndedAt = endedAt;
            closed++;
        }

        return closed;
    }

    /// <summary>
    /// Kiểm tra bất biến <c>SystemPositionId != null ⟺ đúng 1 dòng mở (chưa superseded) trỏ đúng vị trí</c>.
    /// Đọc từ DB (AsNoTracking) nên phải gọi SAU <c>SaveChangesAsync</c> và TRƯỚC <c>CommitAsync</c>.
    /// </summary>
    public static async Task<bool> IsConsistentAsync(
        IApplicationDbContext context,
        Asset asset,
        CancellationToken cancellationToken)
    {
        var openPositionIds = await context.AssetInstallations.AsNoTracking()
            .Where(i => i.AssetId == asset.Id && i.EndedAt == null && i.SupersededAt == null)
            .Select(i => i.SystemPositionId)
            .ToListAsync(cancellationToken);

        return asset.SystemPositionId.HasValue
            ? openPositionIds.Count == 1 && openPositionIds[0] == asset.SystemPositionId.Value
            : openPositionIds.Count == 0;
    }

    /// <summary>
    /// Tên người ghi để denormalize vào <c>LoggedByName</c> ("Họ Tên", fallback Username).
    /// Trả về null khi không có actor (dòng backfill/import không có người ghi) hoặc user đã bị xóa.
    /// </summary>
    public static async Task<string?> ResolveActorNameAsync(
        IApplicationDbContext context,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        if (!userId.HasValue) return null;

        var user = await context.Users.AsNoTracking()
            .Where(u => u.Id == userId.Value)
            .Select(u => new { u.FirstName, u.LastName, u.Username })
            .FirstOrDefaultAsync(cancellationToken);

        if (user == null) return null;

        var fullName = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(fullName) ? user.Username : fullName;
    }
}
