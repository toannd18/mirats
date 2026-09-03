using aspire_react.Server.Domain.Enums;

namespace aspire_react.Server.Domain.Interfaces;

/// <summary>Outcome of a component allocation/return/stock-in operation.
/// [Giai đoạn 3] Moved verbatim from Infrastructure/Services/ComponentAllocationService.cs with
/// the interface — Application handlers consume the service through this contract (the concrete
/// implementation with its FOR UPDATE transactional logic stays in Infrastructure).</summary>
public record ComponentOperationResult(bool Success, string Message, string? ErrorCode = null);

/// <summary>
/// Business rules for Component stock operations, branching on <see cref="TrackingType"/>:
/// Bulk keeps the legacy quantity-pool behaviour; Serial tracks each physical unit individually.
/// Every operation writes an ActionLog in the same SaveChanges call (atomic with the change).
/// The controller wraps calls in an ambient transaction so the change + its audit log are atomic.
/// [Giai đoạn 3] Interface extracted verbatim so ComponentUnits command handlers (Application)
/// delegate to the service WITHOUT touching its concurrency/lock logic.
/// </summary>
public interface IComponentAllocationService
{
    Task<ComponentOperationResult> AllocateAsync(Guid componentId, Guid assetId, int quantity,
        string? serialNo, string? note, Guid createdById, CancellationToken ct = default);

    Task<ComponentOperationResult> ReturnAsync(Guid componentId, Guid? assetId, int quantity,
        string? serialNo, string? note, Guid createdById, CancellationToken ct = default);

    Task<ComponentOperationResult> StockInAsync(Guid componentId, IReadOnlyList<string> serialNumbers,
        string? note, Guid createdById, CancellationToken ct = default);

    Task<ComponentOperationResult> SetUnitStatusAsync(Guid unitId, ComponentUnitStatus status,
        string? note, Guid createdById, CancellationToken ct = default);

    /// <summary>
    /// Soft-deletes a serial unit that has NEVER been checked out (allocation history must stay
    /// intact — such units must be disposed instead). Decrements the parent component's Qty and
    /// writes an ActionLog, all in the same SaveChanges. Enforces company-scoping for the acting user.
    /// </summary>
    Task<ComponentOperationResult> DeleteUnitAsync(Guid unitId, Guid createdById, CancellationToken ct = default);
}
