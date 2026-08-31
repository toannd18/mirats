namespace aspire_react.Server.Domain.Entities;

/// <summary>
/// One seat ("chỗ") of a License. Seats are auto-generated on create (SeatNumber 1..N) — the same
/// convention as ComponentUnit for Component serials. A seat is assigned to EXACTLY ONE of three
/// target kinds at any moment: User, Asset or SystemInfo (the "Hệ thống" convention: always the
/// SystemInfo PARENT — a license applies to the WHOLE system, never a specific SystemPosition).
/// Empty seats have all three fields NULL; the DB CHECK constraint only forbids assigning 2+ targets
/// at once, while the checkout service enforces exactly-one-of-three when assigning.
/// </summary>
public class LicenseSeat
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LicenseId { get; set; }
    public int SeatNumber { get; set; }
    public Guid? UserId { get; set; }
    public Guid? AssetId { get; set; }
    public Guid? SystemInfoId { get; set; }
    public string? Note { get; set; }
    public DateTime? AssignedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
    public DateTime UpdatedAt { get; set; } = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

    // Navigation
    public License License { get; set; } = null!;
    public User? User { get; set; }
    public Asset? Asset { get; set; }
    public SystemInfo? SystemInfo { get; set; }
}