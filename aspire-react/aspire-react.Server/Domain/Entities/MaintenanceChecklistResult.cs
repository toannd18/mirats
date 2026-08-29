using aspire_react.Server.Domain.Interfaces;

namespace aspire_react.Server.Domain.Entities;

/// <summary>
/// One measured checklist result: a device snapshot × a checklist item × an optional standard param × a campaign.
/// MeasuredValue + IsPass (Đạt/Không đạt) + Notes.
/// Unique per (Campaign, DeviceSnapshot, ChecklistItem, StandardParamId) — [MC-9] mỗi tiêu chuẩn kỹ thuật
/// của 1 hạng mục có 1 dòng kết quả riêng; hạng mục không có tiêu chuẩn thì StandardParamId = NULL
/// (1 dòng chung cho cả hạng mục).
/// </summary>
public class MaintenanceChecklistResult : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CampaignId { get; set; }
    public MaintenanceCampaign Campaign { get; set; } = null!;

    /// <summary>The device this row was measured on (immutable snapshot — NOT the live asset).</summary>
    public Guid DeviceSnapshotId { get; set; }
    public MaintenanceCampaignDeviceSnapshot DeviceSnapshot { get; set; } = null!;

    /// <summary>The checklist item being measured (belongs to the campaign's pinned template version).</summary>
    public Guid ChecklistItemId { get; set; }
    public MaintenanceChecklistItem ChecklistItem { get; set; } = null!;

    /// <summary>[MC-9] Tiêu chuẩn kỹ thuật cụ thể của hạng mục; NULL khi hạng mục không có tiêu chuẩn nào.</summary>
    public Guid? StandardParamId { get; set; }
    public MaintenanceStandardParam? StandardParam { get; set; }

    public string? MeasuredValue { get; set; }
    public bool IsPass { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
