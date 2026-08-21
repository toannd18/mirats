namespace aspire_react.Server.Domain.Entities;

/// <summary>
/// Operational counter backing auto-generated Asset Tags (Task ASSET-TAG-AUTO).
/// Keyed by (CompanyId, Year) so each company has its own independent sequence that resets to 1
/// at the start of each new year. Rows are locked FOR UPDATE inside the create transaction so
/// concurrent asset creation never hands out the same sequence (Task O/O-FIX race pattern).
/// </summary>
public class AssetTagCounter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Company owning this counter (null = the company-less floater bucket).</summary>
    public Guid? CompanyId { get; set; }
    public int Year { get; set; }
    public long CurrentSeq { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
