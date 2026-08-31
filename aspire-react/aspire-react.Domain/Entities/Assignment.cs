using aspire_react.Server.Domain.Enums;

namespace aspire_react.Server.Domain.Entities;

public class Assignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssetId { get; set; }
    public AssignmentTargetType TargetType { get; set; }
    public Guid TargetId { get; set; }
    public Guid AssignedById { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public string? Note { get; set; }

    // Navigation
    public Asset Asset { get; set; } = null!;
    public User? AssignedUser { get; set; }
    public User AssignedBy { get; set; } = null!;
}