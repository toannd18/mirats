using aspire_react.Server.Domain.Interfaces;

namespace aspire_react.Server.Domain.Entities;

/// <summary>
/// One checklist line item inside a specific template version (Order + Name + CycleMonths + tools/instruction).
/// Belongs to an immutable version → treated as immutable once the version has campaigns.
/// </summary>
public class MaintenanceChecklistItem : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TemplateVersionId { get; set; }
    public MaintenanceChecklistTemplateVersion TemplateVersion { get; set; } = null!;

    public int Order { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CycleMonths { get; set; }
    public string? ToolsRequired { get; set; }
    public string? Instruction { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<MaintenanceChecklistResult> Results { get; set; } = new List<MaintenanceChecklistResult>();
    /// <summary>[MC-7a] Các vị trí (SystemPosition) item này áp dụng. Rỗng = mọi vị trí (universal).</summary>
    public ICollection<MaintenanceChecklistItemPosition> Positions { get; set; } = new List<MaintenanceChecklistItemPosition>();
    /// <summary>[MC-8] Tiêu chuẩn kỹ thuật THUỘC VỀ hạng mục này (thuộc tính con, không phải danh sách rời cấp Version).</summary>
    public ICollection<MaintenanceStandardParam> StandardParams { get; set; } = new List<MaintenanceStandardParam>();
}
