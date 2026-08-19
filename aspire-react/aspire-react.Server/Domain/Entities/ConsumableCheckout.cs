namespace aspire_react.Server.Domain.Entities;

public class ConsumableCheckout
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConsumableId { get; set; }
    public Guid UserId { get; set; }
    public Guid? AssignedToId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public int Quantity { get; set; } = 1;
    public string? Note { get; set; }
    public DateTime CheckedOutAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Consumable Consumable { get; set; } = null!;
    public User User { get; set; } = null!;
    public User? CreatedByUser { get; set; }
}