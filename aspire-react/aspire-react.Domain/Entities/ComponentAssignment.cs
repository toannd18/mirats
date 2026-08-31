namespace aspire_react.Server.Domain.Entities;

public class ComponentAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ComponentId { get; set; }
    public Guid AssetId { get; set; }
    public int AssignedQty { get; set; } = 1;
    public string? Note { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Component Component { get; set; } = null!;
    public Asset Asset { get; set; } = null!;
}