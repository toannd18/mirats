namespace aspire_react.Server.Application.AssetMaintenances.Queries;

/// <summary>Assignee row — display-name fallback verbatim (First+Last trimmed, else Username).</summary>
public record MaintenanceAssigneeDto(Guid UserId, string Name, DateTime AssignedAt);

/// <summary>Supplier ref ({Id, Name}) — null when no supplier.</summary>
public record MaintenanceSupplierRefDto(Guid Id, string Name);

/// <summary>Asset ref for the asset-scoped list ({Id, AssetTag, Name}) — verbatim (no CompanyName).</summary>
public record MaintenanceAssetRefDto(Guid Id, string AssetTag, string Name);

/// <summary>Asset ref for the aggregated list (+ CompanyName, null when asset has no company).</summary>
public record MaintenanceAssetWithCompanyRefDto(Guid Id, string AssetTag, string Name, string? CompanyName);

/// <summary>
/// List item for GET assets/{assetId}/maintenances — verbatim projection (NO IsClosed key,
/// asset ref WITHOUT CompanyName).
/// </summary>
public record AssetMaintenanceListItemDto(
    Guid Id,
    string Type,
    string Title,
    string? Notes,
    DateTime StartDate,
    DateTime? CompletionDate,
    decimal? Cost,
    bool IsWarranty,
    Guid CompanyId,
    MaintenanceSupplierRefDto? Supplier,
    MaintenanceAssetRefDto Asset,
    Guid? SnapshotSystemInfoId,
    string? SnapshotSystemInfoName,
    Guid? SnapshotSystemPositionId,
    string? SnapshotSystemPositionName,
    Guid? SnapshotLocationId,
    string? SnapshotLocationName,
    Guid? SnapshotAssignedUserId,
    string? SnapshotAssignedUserName,
    Guid? SnapshotDepartmentId,
    string? SnapshotDepartmentName,
    Guid? InspectedById,
    DateTime? InspectedAt,
    string? InspectedByName,
    IReadOnlyList<MaintenanceAssigneeDto> Assignees,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>
/// List item for GET /maintenances — verbatim projection (HAS IsClosed key, asset ref WITH
/// CompanyName). Kept as a separate DTO so the asset-scoped list JSON gains no extra keys.
/// </summary>
public record AllMaintenanceListItemDto(
    Guid Id,
    string Type,
    string Title,
    string? Notes,
    DateTime StartDate,
    DateTime? CompletionDate,
    decimal? Cost,
    bool IsWarranty,
    bool IsClosed,
    Guid CompanyId,
    MaintenanceSupplierRefDto? Supplier,
    MaintenanceAssetWithCompanyRefDto Asset,
    Guid? SnapshotSystemInfoId,
    string? SnapshotSystemInfoName,
    Guid? SnapshotSystemPositionId,
    string? SnapshotSystemPositionName,
    Guid? SnapshotLocationId,
    string? SnapshotLocationName,
    Guid? SnapshotAssignedUserId,
    string? SnapshotAssignedUserName,
    Guid? SnapshotDepartmentId,
    string? SnapshotDepartmentName,
    Guid? InspectedById,
    DateTime? InspectedAt,
    string? InspectedByName,
    IReadOnlyList<MaintenanceAssigneeDto> Assignees,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Live asset context (computed on the fly, never stored) — verbatim currentContext shape.</summary>
public record MaintenanceCurrentContextDto(
    Guid? SystemInfoId,
    string? SystemInfoName,
    Guid? SystemPositionId,
    string? SystemPositionName,
    Guid? LocationId,
    string? LocationName,
    Guid? AssignedUserId,
    string? AssignedUserName,
    Guid? DepartmentId,
    string? DepartmentName);

/// <summary>Detail for GET /maintenances/{id} — verbatim (+ ClosedAt/ClosedById + currentContext).</summary>
public record MaintenanceDetailDto(
    Guid Id,
    string Type,
    string Title,
    string? Notes,
    DateTime StartDate,
    DateTime? CompletionDate,
    decimal? Cost,
    bool IsWarranty,
    bool IsClosed,
    DateTime? ClosedAt,
    Guid? ClosedById,
    Guid CompanyId,
    MaintenanceSupplierRefDto? Supplier,
    MaintenanceAssetRefDto Asset,
    Guid? SnapshotSystemInfoId,
    string? SnapshotSystemInfoName,
    Guid? SnapshotSystemPositionId,
    string? SnapshotSystemPositionName,
    Guid? SnapshotLocationId,
    string? SnapshotLocationName,
    Guid? SnapshotAssignedUserId,
    string? SnapshotAssignedUserName,
    Guid? SnapshotDepartmentId,
    string? SnapshotDepartmentName,
    Guid? InspectedById,
    DateTime? InspectedAt,
    string? InspectedByName,
    IReadOnlyList<MaintenanceAssigneeDto> Assignees,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    MaintenanceCurrentContextDto CurrentContext);
