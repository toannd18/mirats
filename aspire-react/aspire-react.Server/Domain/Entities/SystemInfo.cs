namespace aspire_react.Server.Domain.Entities;

public class SystemInfo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? CompanyId { get; set; }

    /// <summary>
    /// Next scheduled maintenance due date (computed): derived from the most recent COMPLETED
    /// MaintenanceCampaign + the configured cycle — never auto-created, only surfaced as an
    /// overdue warning (Dashboard/badge). NULL when no campaign has been completed yet.
    /// </summary>
    public DateTime? NextMaintenanceDueDate { get; set; }

    public Company? Company { get; set; }
    public ICollection<SystemPosition> Positions { get; set; } = new List<SystemPosition>();
}
