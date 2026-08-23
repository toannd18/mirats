using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Infrastructure.Services;

/// <summary>Outcome of a consumable checkout operation.</summary>
public record ConsumableCheckoutResult(bool Success, string Message, string? ErrorCode = null, Guid? CheckoutId = null);

/// <summary>
/// Business rules for Consumable checkout — mirrors the Accessory/License standard:
/// target must be a User, must exist, and (when the consumable is company-scoped) must belong
/// to the same company. Every checkout writes a complete ActionLog (target user + company) via
/// the centralized <see cref="IActionLogService"/> in the same SaveChanges the controller
/// commits inside its ambient transaction.
/// </summary>
public interface IConsumableAllocationService
{
    Task<ConsumableCheckoutResult> CheckoutAsync(Guid consumableId, Guid? userId, int quantity,
        string? note, Guid createdById, CancellationToken ct = default);
}

public class ConsumableAllocationService : IConsumableAllocationService
{
    private readonly AppDbContext _context;
    private readonly IActionLogService _actionLogService;
    private readonly ICompanyScopeService _companyScope;

    public ConsumableAllocationService(AppDbContext context, IActionLogService actionLogService, ICompanyScopeService companyScope)
    {
        _context = context;
        _actionLogService = actionLogService;
        _companyScope = companyScope;
    }

    public async Task<ConsumableCheckoutResult> CheckoutAsync(Guid consumableId, Guid? userId, int quantity,
        string? note, Guid createdById, CancellationToken ct = default)
    {
        // Task O-FIX: lock the consumable row FOR UPDATE (mirroring the Asset checkout pattern) so two
        // concurrent checkouts cannot both read the same remaining and overcommit the last unit. On EF
        // InMemory (no raw SQL) fall back to a normal load — real locking is covered by Category=Concurrency
        // tests against real Postgres.
        var consumable = _context.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
            ? await _context.Consumables.FirstOrDefaultAsync(c => c.Id == consumableId, ct)
            : await _context.Consumables
                .FromSqlRaw("SELECT * FROM consumables WHERE \"Id\" = {0} FOR UPDATE", consumableId)
                .FirstOrDefaultAsync(ct);
        if (consumable == null)
            return new ConsumableCheckoutResult(false, "Consumable not found.", "NOT_FOUND");

        // [SEC-FIX S2/S4-S6, 2026-08-23] Actor-scope: a regular user may only check out consumables
        // of their own company (or floater); Superuser bypasses. Previously CheckoutAsync only
        // validated consumable↔target-user company — a user from company A could consume a
        // company-B consumable by id (assigning it to a company-B user).
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && consumable.CompanyId.HasValue && consumable.CompanyId.Value != userCompanyId.Value)
            return new ConsumableCheckoutResult(false, "Consumable not found.", "NOT_FOUND");

        // ─── Status gate: only Confirmed consumables can be allocated. A Pending (Chờ xác nhận)
        // consumable must be confirmed first — mirrors the business rule enforced on the UI (the
        // "Cấp phát" button is hidden while Pending) so direct API calls cannot bypass it.
        if (consumable.Status != ConsumableStatus.Confirmed)
            return new ConsumableCheckoutResult(false,
                "Vật tư chưa được xác nhận — không thể cấp phát. Hãy xác nhận vật tư trước.", "CONSUMABLE_NOT_CONFIRMED");

        if (quantity <= 0)
            return new ConsumableCheckoutResult(false, $"S��` l�����ng c���p phA�t ph���i l��>n h��n 0.", "INVALID_QUANTITY");

        var checkedOut = await _context.ConsumableCheckouts
            .Where(c => c.ConsumableId == consumableId)
            .SumAsync(c => (int?)c.Quantity, ct) ?? 0;
        var remaining = consumable.Qty - checkedOut;
        if (quantity > remaining)
            return new ConsumableCheckoutResult(false,
                $"Insufficient stock. Remaining: {remaining}", "INSUFFICIENT_STOCK");

        var targetUserId = userId ?? createdById;
        if (targetUserId == Guid.Empty)
            return new ConsumableCheckoutResult(false, "Người nhận không hợp lệ.", "TARGET_REQUIRED");

        // ─── Target user must exist ───
        var target = await _context.Users.AsNoTracking()
            .Select(u => new { u.Id, u.CompanyId })
            .FirstOrDefaultAsync(u => u.Id == targetUserId, ct);
        if (target == null)
            return new ConsumableCheckoutResult(false, "Người dùng không tồn tại.", "TARGET_NOT_FOUND");

        // ─── Company Isolation ─── Mirrors the License/Accessory checkout standard:
        // when the consumable is scoped to a company, the receiving user must belong to it.
        if (consumable.CompanyId.HasValue && target.CompanyId != consumable.CompanyId)
            return new ConsumableCheckoutResult(false,
                "Người dùng không thuộc cùng công ty với vật tư.", "CONSUMABLE_COMPANY_MISMATCH");

        var co = new ConsumableCheckout
        {
            ConsumableId = consumableId,
            UserId = targetUserId,
            CreatedByUserId = createdById,
            Quantity = quantity,
            Note = note
        };
        _context.ConsumableCheckouts.Add(co);

        // Complete audit trail via the centralized service (same pattern as Accessory/License):
        // TargetType/TargetId record WHO received it so the system history can resolve the user.
        _actionLogService.LogAction(
            itemType: ItemType.Consumable,
            itemId: consumableId,
            actionType: ActionType.Checkout,
            loggedByUserId: createdById,
            targetType: AssignmentTargetType.User,
            targetId: targetUserId,
            note: note,
            logMeta: JsonSerializer.Serialize(new { quantity }),
            companyId: consumable.CompanyId);

        await _context.SaveChangesAsync(ct);
        return new ConsumableCheckoutResult(true, $"{quantity} consumable(s) checked out.", CheckoutId: co.Id);
    }
}
