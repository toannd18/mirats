using aspire_react.Server.Domain.Interfaces;

namespace aspire_react.Server.Domain.Entities;

/// <summary>
/// MC-7a — Phạm vi áp dụng của một ChecklistItem theo VỊ TRÍ (SystemPosition) trong hệ thống.
/// <para>
/// Đây là CẤU HÌNH TEMPLATE (không phải snapshot lịch sử): liên hệ trực tiếp tới vị trí THẬT
/// đang tồn tại, nên SystemPositionId là FK với RESTRICT — một vị trí đang được ChecklistItem
/// tham chiếu sẽ KHÔNG thể bị xóa (delete-guard, mirror pattern Company AR-2). ItemId là FK
/// CASCADE: xóa Item (chỉ xảy ra khi version chưa có campaign) là xóa luôn khai báo vị trí.
/// </para>
/// <para>
/// Quy ước: Item KHÔNG có dòng nào trong bảng này = áp dụng MỌI vị trí (universal) — tương thích
/// ngược với các item/version tạo trước feature này (MC-7a).
/// </para>
/// </summary>
public class MaintenanceChecklistItemPosition : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ItemId { get; set; }
    public MaintenanceChecklistItem Item { get; set; } = null!;

    /// <summary>Vị trí áp dụng (FK RESTRICT — không xóa được vị trí đang được template dùng).</summary>
    public Guid SystemPositionId { get; set; }
    public SystemPosition SystemPosition { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}