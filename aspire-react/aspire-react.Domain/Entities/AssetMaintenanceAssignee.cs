namespace aspire_react.Server.Domain.Entities;

/// <summary>
/// Many-to-many join: a maintenance record may be assigned to up to 5 users (the workers performing
/// the repair). The max count is validated at the API layer (no DB trigger); a unique
/// (MaintenanceId, UserId) index prevents duplicates. Assignees are replace-all via PUT and become
/// immutable once the maintenance record is closed (the IsClosed guard rejects all edits).
/// </summary>
public class AssetMaintenanceAssignee
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MaintenanceId { get; set; }
    public AssetMaintenance Maintenance { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime AssignedAt { get; set; } = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
}