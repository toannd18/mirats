using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;

namespace aspire_react.Server.Domain.Entities;

/// <summary>
/// One maintenance run (đợt bảo dưỡng) for a SystemInfo against a SPECIFIC template version — the version
/// is pinned at creation time (TemplateVersionId locked; the version is immutable once referenced).
/// </summary>
public class MaintenanceCampaign : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SystemInfoId { get; set; }
    public SystemInfo SystemInfo { get; set; } = null!;

    /// <summary>Pinned at creation — never changed. Points to an immutable template version.</summary>
    public Guid TemplateVersionId { get; set; }
    public MaintenanceChecklistTemplateVersion TemplateVersion { get; set; } = null!;

    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? BatchNumber { get; set; }

    /// <summary>Owner company — server-set from SystemInfo.CompanyId at creation (floater = null).</summary>
    public Guid? CompanyId { get; set; }
    public Company? Company { get; set; }

    public Guid? ReviewerId { get; set; }
    public User? Reviewer { get; set; }

    public MaintenanceCampaignStatus Status { get; set; } = MaintenanceCampaignStatus.InProgress;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<MaintenanceCampaignExecutor> Executors { get; set; } = new List<MaintenanceCampaignExecutor>();
    public ICollection<MaintenanceCampaignDeviceSnapshot> DeviceSnapshots { get; set; } = new List<MaintenanceCampaignDeviceSnapshot>();
    public ICollection<MaintenanceChecklistResult> Results { get; set; } = new List<MaintenanceChecklistResult>();
}
