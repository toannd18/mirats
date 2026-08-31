using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;

namespace aspire_react.Server.Domain.Entities;

/// <summary>
/// A single physical unit of a serial-tracked Component. Bulk components do not use this table.
/// Soft-deleted via <see cref="DeletedAt"/> — units are never hard-deleted (audit trail).
/// </summary>
public class ComponentUnit : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ComponentId { get; set; }
    public Component Component { get; set; } = null!;
    public string? SerialNo { get; set; }
    public ComponentUnitStatus Status { get; set; } = ComponentUnitStatus.InStock;
    /// <summary>Asset this unit is currently allocated to (null when not allocated).</summary>
    public Guid? CurrentAssetId { get; set; }
    public Asset? CurrentAsset { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
    public DateTime UpdatedAt { get; set; } = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
    public DateTime? DeletedAt { get; set; }
}
