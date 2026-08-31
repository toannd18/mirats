using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;

namespace aspire_react.Server.Domain.Entities;

/// <summary>
/// A technical standard row ("bảng tiêu chuẩn" from the source doc — CPU/RAM/HDD/Net load per device type).
/// [MC-8] Mỗi tiêu chuẩn giờ THUỘC VỀ ĐÚNG 1 ChecklistItem cụ thể (FK ChecklistItemId CASCADE) —
/// tiêu chuẩn là thuộc tính CON của hạng mục, không còn là danh sách rời rạc cấp Version.
/// Version được suy ra qua Item.TemplateVersionId (không lưu trùng lặp TemplateVersionId ở đây).
/// [MC-10] Ngưỡng cảnh báo đi từ text tự do → cấu trúc: (ThresholdOperator, ThresholdValue: decimal)
/// BẮT BUỘC — cho phép máy suy Đạt/Không đạt = so sánh(MeasuredValue, ThresholdValue) theo Operator.
/// NominalValue giữ text (định mức tham chiếu, không dùng để so sánh).
/// Immutable once the version has campaigns.
/// </summary>
public class MaintenanceStandardParam : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChecklistItemId { get; set; }
    public MaintenanceChecklistItem ChecklistItem { get; set; } = null!;

    public string ParamName { get; set; } = string.Empty;
    public string? NominalValue { get; set; }
    /// <summary>[MC-10] Toán tử so sánh ngưỡng: &lt;, &lt;=, &gt;, &gt;=, =.</summary>
    public MaintenanceThresholdOperator ThresholdOperator { get; set; }
    /// <summary>[MC-10] Giá trị ngưỡng (số thuần, không kèm unit — Unit nằm ở field riêng).</summary>
    public decimal ThresholdValue { get; set; }
    public string? CheckMethod { get; set; }
    public string? Unit { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
