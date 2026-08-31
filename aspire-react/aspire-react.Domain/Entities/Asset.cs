using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;

namespace aspire_react.Server.Domain.Entities;

public class Asset : IAuditable, ICompanyable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AssetTag { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Serial { get; set; }
    public string? Image { get; set; }

    // Foreign keys
    public Guid? ModelId { get; set; }
    public Guid? LocationId { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? CurrentAssignmentId { get; set; }
    public Guid? SystemPositionId { get; set; }

    // Lifecycle
    public AssetStatus Status { get; set; } = AssetStatus.Pending;
    public bool IsConfirmed { get; set; }

    // Financial
    public decimal? PurchaseCost { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public int? WarrantyMonths { get; set; }
    public DateTime? AssetEolDate { get; set; }
    public bool EolExplicit { get; set; }

    // Dates
    public DateTime? LastCheckout { get; set; }
    public DateTime? LastCheckin { get; set; }
    public DateTime? LastAuditDate { get; set; }
    public DateTime? NextAuditDate { get; set; }

    // Counters
    public int CheckinCounter { get; set; }
    public int CheckoutCounter { get; set; }
    public int RequestsCounter { get; set; }

    // Flags
    public bool Physical { get; set; } = true;
    public bool Requestable { get; set; }

    // Other
    public string? Accepted { get; set; }
    public string? OrderNumber { get; set; }
    public string? Notes { get; set; }

    // Navigation
    public AssetModel? Model { get; set; }
    public Location? Location { get; set; }
    public Supplier? Supplier { get; set; }
    public Company? Company { get; set; }
    public Assignment? CurrentAssignment { get; set; }
    public SystemPosition? SystemPosition { get; set; }
    public ICollection<Assignment> ChildAssignments { get; set; } = new List<Assignment>();
    public ICollection<ActionLog> ActionLogs { get; set; } = new List<ActionLog>();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}