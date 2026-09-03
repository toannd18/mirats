namespace aspire_react.Server.Domain.Interfaces;

/// <summary>Outcome of a consumable checkout operation.
/// [Giai đoạn 3] Moved verbatim from Infrastructure/Services/ConsumableAllocationService.cs with
/// the interface — the concrete implementation (FOR UPDATE-style transactional checkout logic)
/// stays in Infrastructure.</summary>
public record ConsumableCheckoutResult(bool Success, string Message, string? ErrorCode = null, Guid? CheckoutId = null);

/// <summary>
/// Business rules for Consumable checkout — mirrors the Accessory/License standard:
/// target must be a User, must exist, and (when the consumable is company-scoped) must belong
/// to the same company. Every checkout writes a complete ActionLog (target user + company) via
/// the centralized <see cref="IActionLogService"/> in the same SaveChanges the controller
/// commits inside its ambient transaction.
/// [Giai đoạn 3] Interface extracted verbatim so the Consumables checkout command (Application)
/// delegates without referencing Infrastructure.
/// </summary>
public interface IConsumableAllocationService
{
    Task<ConsumableCheckoutResult> CheckoutAsync(Guid consumableId, Guid? userId, int quantity,
        string? note, Guid createdById, CancellationToken ct = default);
}
