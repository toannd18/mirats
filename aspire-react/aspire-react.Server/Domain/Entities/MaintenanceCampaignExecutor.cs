using aspire_react.Server.Domain.Interfaces;

namespace aspire_react.Server.Domain.Entities;

/// <summary>Join: a campaign's executors (người thực hiện) — many users per campaign, unique (CampaignId, UserId).</summary>
public class MaintenanceCampaignExecutor : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CampaignId { get; set; }
    public MaintenanceCampaign Campaign { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
