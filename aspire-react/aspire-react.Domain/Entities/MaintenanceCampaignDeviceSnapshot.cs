using aspire_react.Server.Domain.Interfaces;

namespace aspire_react.Server.Domain.Entities;

/// <summary>
/// IMMUTABLE snapshot of the assets installed at the system's positions AT THE MOMENT the campaign is
/// created (Phần II của phiếu). The asset/position are copied as denormalized text + plain Guids (no FK)
/// so the snapshot survives later asset moves/deletes and position changes — results always reference this
/// snapshot, never the live asset.
/// </summary>
public class MaintenanceCampaignDeviceSnapshot : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CampaignId { get; set; }
    public MaintenanceCampaign Campaign { get; set; } = null!;

    /// <summary>Traceability to the live asset at capture time (no FK — snapshot must stay independent).</summary>
    public Guid AssetId { get; set; }
    public string AssetTag { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public string? Serial { get; set; }
    public string? ModelNumber { get; set; }

    public Guid? SystemPositionId { get; set; }
    public string? SystemPositionName { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<MaintenanceChecklistResult> Results { get; set; } = new List<MaintenanceChecklistResult>();
}
