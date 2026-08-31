using aspire_react.Server.Domain.Interfaces;

namespace aspire_react.Server.Domain.Entities;

/// <summary>
/// One immutable snapshot of a template's checklist definition. Editing the process creates a NEW version
/// (VersionNumber+1) — never mutates an existing one once a MaintenanceCampaign references it.
/// </summary>
public class MaintenanceChecklistTemplateVersion : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TemplateId { get; set; }
    public MaintenanceChecklistTemplate Template { get; set; } = null!;

    public int VersionNumber { get; set; }
    public DateTime? EffectiveFrom { get; set; }

    /// <summary>
    /// UTC instant this version was published. NULL = DRAFT (chưa publish): items/params còn tự do
    /// sửa/xóa và version KHÔNG thể được campaign tham chiếu. Được set một lần duy nhất bởi
    /// publish endpoint (MC-2) — không bao giờ bị xóa về null sau đó. [MC-2]
    /// </summary>
    public DateTime? PublishedAt { get; set; }

    /// <summary>Only ONE version per template may be "current" (the one new campaigns pick by default).</summary>
    public bool IsCurrent { get; set; }
    public Guid CreatedById { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<MaintenanceChecklistItem> Items { get; set; } = new List<MaintenanceChecklistItem>();
    public ICollection<MaintenanceCampaign> Campaigns { get; set; } = new List<MaintenanceCampaign>();
}
