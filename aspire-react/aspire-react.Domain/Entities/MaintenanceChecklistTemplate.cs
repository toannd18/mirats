using aspire_react.Server.Domain.Interfaces;

namespace aspire_react.Server.Domain.Entities;

/// <summary>
/// A maintenance checklist template for ONE system (SystemInfo). Versioned: every published edit of the
/// checklist/standards creates a NEW MaintenanceChecklistTemplateVersion — versions already referenced by
/// a MaintenanceCampaign are IMMUTABLE (never edited/deleted; enforced at the API layer).
/// Company-scoped: CompanyId = null means a "floater" template usable by any company.
/// </summary>
public class MaintenanceChecklistTemplate : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    /// <summary>The system this template governs (MC-1 scope: per-SystemInfo; no "SystemType" concept yet).</summary>
    public Guid SystemInfoId { get; set; }
    public SystemInfo SystemInfo { get; set; } = null!;

    /// <summary>Owner company (nullable = floater/shared across companies).</summary>
    public Guid? CompanyId { get; set; }
    public Company? Company { get; set; }

    public bool IsActive { get; set; } = true;
    public Guid CreatedById { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<MaintenanceChecklistTemplateVersion> Versions { get; set; } = new List<MaintenanceChecklistTemplateVersion>();
}
