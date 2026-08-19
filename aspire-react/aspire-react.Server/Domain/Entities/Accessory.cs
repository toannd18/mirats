using System.ComponentModel.DataAnnotations.Schema;
using aspire_react.Server.Domain.Interfaces;

namespace aspire_react.Server.Domain.Entities;

public class Accessory : IAuditable, ICompanyable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? ItemNo { get; set; }
    public string? Image { get; set; }
    public string? ModelNumber { get; set; }
    public string? OrderNumber { get; set; }

    public Guid? CategoryId { get; set; }
    public Guid? ManufacturerId { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? LocationId { get; set; }
    public Guid? CompanyId { get; set; }

    public int Qty { get; set; }
    public int MinAmt { get; set; }

    public decimal? PurchaseCost { get; set; }
    public DateTime? PurchaseDate { get; set; }

    public string? Notes { get; set; }

    // Navigation
    public Category? Category { get; set; }
    public Manufacturer? Manufacturer { get; set; }
    public Supplier? Supplier { get; set; }
    public Location? Location { get; set; }
    public Company? Company { get; set; }
    public ICollection<AccessoryCheckout> Checkouts { get; set; } = new List<AccessoryCheckout>();

    [NotMapped] public int Remaining => Qty - Checkouts.Sum(c => c.AssignedQty - c.ReturnedQty);
    [NotMapped] public double PercentRemaining => Qty > 0 ? Math.Round((double)Remaining / Qty * 100, 2) : 0;
    [NotMapped] public bool IsLowStock => Remaining <= MinAmt;
    [NotMapped] public decimal? TotalCost => PurchaseCost.HasValue ? Qty * PurchaseCost.Value : null;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}