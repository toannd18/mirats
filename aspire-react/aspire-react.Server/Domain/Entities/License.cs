using aspire_react.Server.Domain.Interfaces;

namespace aspire_react.Server.Domain.Entities;

public class License : IAuditable, ICompanyable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Serial { get; set; }
    public int Seats { get; set; }
    /// <summary>Whether a checked-out seat may be returned and re-assigned to another target.
    /// When false (e.g. an OEM license locked to one machine), checkin is rejected with
    /// LICENSE_NOT_REASSIGNABLE.</summary>
    public bool Reassignable { get; set; } = true;
    public DateTime? ExpirationDate { get; set; }
    public DateTime? TerminationDate { get; set; }
    public decimal? PurchaseCost { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public string? OrderNumber { get; set; }
    public string? Notes { get; set; }
    /// <summary>Warning threshold: when available seats &lt;= MinSeats, the UI shows a low-seat warning.</summary>
    public int? MinSeats { get; set; }

    public Guid? SupplierId { get; set; }
    public Guid? ManufacturerId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? CompanyId { get; set; }

    // Navigation
    public Supplier? Supplier { get; set; }
    public Manufacturer? Manufacturer { get; set; }
    public Category? Category { get; set; }
    public Company? Company { get; set; }
    public ICollection<LicenseSeat> LicenseSeats { get; set; } = new List<LicenseSeat>();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }
}