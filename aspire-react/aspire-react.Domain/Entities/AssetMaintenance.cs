using aspire_react.Server.Domain.Enums;

namespace aspire_react.Server.Domain.Entities;

/// <summary>
/// A single maintenance/repair/upgrade event for an Asset (Snipe-IT style).
/// Captures an immutable snapshot of the asset context (system 2 levels, location, user,
/// department) at the moment the record is created — even if the asset is later moved,
/// the maintenance history keeps the correct context it was under at the time.
/// </summary>
public class AssetMaintenance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssetId { get; set; }
    public Asset Asset { get; set; } = null!;

    public AssetMaintenanceType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public Guid? SupplierId { get; set; }   // contractor/supplier performing the work
    public Supplier? Supplier { get; set; }

    /// <summary>
    /// Access-control company (non-nullable). Server sets it = Asset.CompanyId at creation time,
    /// NEVER client-chosen and LOCKED afterwards. Distinct from the Snapshot* fields (which are
    /// for history display) — this field exists purely to scope visibility by company for
    /// regular users. Guid.Empty = the asset had no company (floater) → visible to everyone.
    /// </summary>
    public Guid CompanyId { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime? CompletionDate { get; set; }   // null = in progress
    public decimal? Cost { get; set; }
    public bool IsWarranty { get; set; }

    // ─── Close / Lock (audit trail protection) ───
    // Closing freezes the record against ANY further edit (PUT rejects with MAINTENANCE_CLOSED).
    // Only closable once CompletionDate is set. Reopen is Superuser-only and keeps ClosedAt/ClosedById
    // as the history of the most recent close (each close/reopen is audited via ActionLog).
    public bool IsClosed { get; set; }
    public DateTime? ClosedAt { get; set; }
    public Guid? ClosedById { get; set; }

    // ─── Independent inspection step (before close) ───
    // Workflow: Hoàn thành (CompletionDate) → Kiểm tra (Inspect) → Đóng (Close) — 3 distinct steps.
    // Inspection is an independent approval that may be repeated (overwrites) BEFORE close; unlike
    // Close it does NOT lock anything. Close now requires both CompletionDate AND InspectedById.
    public Guid? InspectedById { get; set; }
    public User? InspectedBy { get; set; }
    public DateTime? InspectedAt { get; set; }

    /// <summary>Maintenance workers (max 5, enforced at the API layer). Immutable once the record is closed.</summary>
    public ICollection<AssetMaintenanceAssignee> Assignees { get; set; } = new List<AssetMaintenanceAssignee>();

    // ─── Context snapshot captured at creation time (immutable afterwards) ───
    // SystemPosition is a child of SystemInfo (1 SystemInfo has many SystemPosition) —
    // BOTH levels are snapshotted separately because an asset may be installed at a
    // specific SystemPosition; storing only SystemInfo would lose the sub-location.
    public Guid? SnapshotSystemInfoId { get; set; }
    public string? SnapshotSystemInfoName { get; set; }
    public Guid? SnapshotSystemPositionId { get; set; }
    public string? SnapshotSystemPositionName { get; set; }
    public Guid? SnapshotLocationId { get; set; }
    public string? SnapshotLocationName { get; set; }
    public Guid? SnapshotAssignedUserId { get; set; }
    public string? SnapshotAssignedUserName { get; set; }
    public Guid? SnapshotDepartmentId { get; set; }
    public string? SnapshotDepartmentName { get; set; }

    public Guid CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
    public DateTime UpdatedAt { get; set; } = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
    public DateTime? DeletedAt { get; set; }
}
