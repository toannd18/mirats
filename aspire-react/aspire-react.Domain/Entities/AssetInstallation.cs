using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;

namespace aspire_react.Server.Domain.Entities;

/// <summary>
/// [LL-1] Lý lịch lắp đặt thiết bị — mỗi dòng = MỘT LẦN gắn một Asset vào một SystemPosition.
/// <para>
/// Mô hình KHOẢNG (interval): <see cref="StartedAt"/> → <see cref="EndedAt"/>.
/// <c>EndedAt == null</c> nghĩa là đang gắn (khoảng mở). Tháo/điều chuyển = đặt <c>EndedAt</c>
/// cho dòng đang mở rồi mở dòng mới (2 bản ghi, KHÔNG có EventType).
/// </para>
/// <para>
/// Bảng CHỈ GHI THÊM (append-only). Chỉ cho phép ĐÚNG 2 chuyển trạng thái, mỗi cái đúng 1 lần:
/// <list type="bullet">
///   <item><c>EndedAt</c>: null → giá trị (kết thúc lần gắn).</item>
///   <item><c>SupersededAt</c>: null → giá trị (bản ghi bị ĐÍNH CHÍNH thay thế).</item>
/// </list>
/// Ghi sai KHÔNG sửa/xóa: đặt <c>SupersededAt</c> cho bản sai TRƯỚC, rồi INSERT bản đúng
/// (<see cref="CorrectsEntryId"/> trỏ bản sai), cùng một transaction, có ghi ActionLog.
/// </para>
/// <para>
/// Ràng buộc chống chồng khoảng: exclusion constraint partial (<c>SupersededAt IS NULL</c>) trên
/// <c>(AssetId, tstzrange(StartedAt, EndedAt, '[)'))</c> — tại một thời điểm một Asset chỉ gắn tối
/// đa một vị trí. Ràng buộc được tạo bằng raw SQL trong migration (EF Core không biểu diễn được
/// exclusion constraint qua Fluent API).
/// </para>
/// </summary>
public class AssetInstallation : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Asset được gắn. FK <b>RESTRICT</b> (KHÔNG CASCADE): xóa cứng một asset đã có lý lịch lắp đặt
    /// bị chặn ở API bằng 400 <c>ASSET_HAS_INSTALLATION_HISTORY</c> (guard trước để không lộ 500 FK thô).
    /// </summary>
    public Guid AssetId { get; set; }
    public Asset Asset { get; set; } = null!;

    /// <summary>
    /// Vị trí gắn. FK <b>SET NULL</b>: lịch sử sống sót khi vị trí bị xóa — tên vẫn còn nhờ
    /// denormalize (<see cref="SystemPositionName"/>), cùng lý do như DeviceSnapshot của bảo dưỡng.
    /// </summary>
    public Guid? SystemPositionId { get; set; }
    public SystemPosition? SystemPosition { get; set; }

    // ─── Denormalized snapshot (sống sót sau khi vị trí/hệ thống bị đổi tên hoặc xóa) ───
    public string? SystemPositionName { get; set; }
    public Guid? SystemInfoId { get; set; }
    public string? SystemInfoName { get; set; }

    /// <summary>Thời điểm bắt đầu gắn (timestamp with time zone, Kind=UTC).</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>Thời điểm tháo/điều chuyển. <c>null</c> = đang gắn (khoảng mở).</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>Đánh dấu bản ghi bị đính chính thay thế (null → giá trị, đúng 1 lần).</summary>
    public DateTime? SupersededAt { get; set; }

    /// <summary>Bản ghi đính chính (bản MỚI trỏ về bản SAI). Self-FK RESTRICT.</summary>
    public Guid? CorrectsEntryId { get; set; }
    public AssetInstallation? CorrectsEntry { get; set; }

    /// <summary>Ghi chú tự do (lý do tháo/điều chuyển, nội dung đính chính...).</summary>
    public string? Note { get; set; }

    /// <summary>Người ghi. <b>Nullable</b> — dòng backfill không có người ghi.</summary>
    public Guid? LoggedById { get; set; }
    public User? LoggedBy { get; set; }

    /// <summary>
    /// Tên người ghi (denormalize) — sống sót khi user bị xóa/đổi tên, cùng lý do với
    /// <see cref="SystemPositionName"/>. FK <see cref="LoggedById"/> là SET NULL.
    /// </summary>
    public string? LoggedByName { get; set; }

    /// <summary>Nguồn phát sinh bản ghi (Api / Import / Backfill).</summary>
    public AssetInstallationSource Source { get; set; } = AssetInstallationSource.Api;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
