using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;

namespace aspire_react.Server.Domain.Entities;

/// <summary>
/// [FEAT-SPARE 3] Append-only ledger of spare parts issued to / returned from a system.
/// Exactly one of <see cref="ComponentId"/>/<see cref="ConsumableId"/> is populated. The row is
/// never updated or deleted — stock is derived from the signed sum of movements.
/// </summary>
public class SystemSparePartMovement : ICompanyable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? CompanyId { get; set; }
    public Guid SystemInfoId { get; set; }
    public Guid? ComponentId { get; set; }
    public Guid? ConsumableId { get; set; }
    public int Quantity { get; set; }
    public SparePartMovementDirection Direction { get; set; }
    public string? Note { get; set; }
    public Guid CreatedById { get; set; }
    /// <summary>Stored as <c>timestamp with time zone</c>.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public SystemInfo SystemInfo { get; set; } = null!;
    public Component? Component { get; set; }
    public Consumable? Consumable { get; set; }
    public User? CreatedBy { get; set; }
    public Company? Company { get; set; }
}
