using aspire_react.Server.Domain.Interfaces;

namespace aspire_react.Server.Domain.Entities;

/// <summary>
/// [FEAT-SPARE 1] Per-system spare-part quota. Exactly one of ComponentId/ConsumableId must be set.
/// [FEAT-SPARE 3] Issued/returned quantities live in the append-only <see cref="SystemSparePartMovement"/> ledger.
/// </summary>
public class SystemSparePartRequirement : IAuditable, ICompanyable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? CompanyId { get; set; }
    public Guid SystemInfoId { get; set; }
    public Guid? ComponentId { get; set; }
    public Guid? ConsumableId { get; set; }
    public int MinQty { get; set; }
    public string? Note { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public SystemInfo SystemInfo { get; set; } = null!;
    public Component? Component { get; set; }
    public Consumable? Consumable { get; set; }
    public Company? Company { get; set; }
}
