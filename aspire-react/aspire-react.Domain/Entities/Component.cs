using System.ComponentModel.DataAnnotations.Schema;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;

namespace aspire_react.Server.Domain.Entities;

public class Component : IAuditable, ICompanyable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Serial { get; set; }
    public string? ItemNo { get; set; }
    /// <summary>
    /// Total tracked quantity. For Bulk components this is the raw input quantity.
    /// For Serial components this is kept in sync with the number of ComponentUnit rows
    /// (Qty is read-only for Serial — never a direct user input).
    /// </summary>
    public int Qty { get; set; }
    public int MinAmt { get; set; }
    public decimal? PurchaseCost { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public string? OrderNumber { get; set; }
    public string? Notes { get; set; }

    /// <summary>Bulk (quantity pool) or Serial (per-unit tracking). Defaults to Bulk.</summary>
    public TrackingType TrackingType { get; set; } = TrackingType.Bulk;

    public Guid? CategoryId { get; set; }
    public Guid? ManufacturerId { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? LocationId { get; set; }
    public Guid? CompanyId { get; set; }
    public string? ModelNumber { get; set; }

    // Navigation
    public Category? Category { get; set; }
    public Manufacturer? Manufacturer { get; set; }
    public Supplier? Supplier { get; set; }
    public Location? Location { get; set; }
    public Company? Company { get; set; }
    public ICollection<ComponentAssignment> Assignments { get; set; } = new List<ComponentAssignment>();
    /// <summary>Serial-tracked units (empty for Bulk components).</summary>
    public ICollection<ComponentUnit> Units { get; set; } = new List<ComponentUnit>();

    [NotMapped] public int Remaining => Qty - Assignments.Sum(a => a.AssignedQty);
    [NotMapped] public double PercentRemaining => Qty > 0 ? Math.Round((double)Remaining / Qty * 100, 2) : 0;
    [NotMapped] public bool IsLowStock => Remaining <= MinAmt;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}