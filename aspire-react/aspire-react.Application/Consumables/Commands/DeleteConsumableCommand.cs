using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Consumables.Commands;

/// <summary>
/// [Giai đoạn 3 — Nặng] DELETE /api/v1/consumables/{id} (extracted from
/// ConsumablesController.Delete). Verbatim: scope → 404; Confirmed → 400; has-checkouts →
/// CONSUMABLE_HAS_CHECKOUTS (FK CASCADE would wipe history). LogAction verbatim.
/// </summary>
public record DeleteConsumableCommand(Guid Id, Guid CurrentUserId) : IRequest<ConsumableResult>;

public class DeleteConsumableCommandHandler : IRequestHandler<DeleteConsumableCommand, ConsumableResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public DeleteConsumableCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<ConsumableResult> Handle(DeleteConsumableCommand request, CancellationToken cancellationToken)
    {
        var c = await _context.Consumables.FindAsync(new object?[] { request.Id }, cancellationToken);
        if (c == null)
            return new ConsumableResult(false, "Consumable not found.", "NOT_FOUND");

        // Company scoping: a regular user may only delete consumables of their own company (or floater).
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && c.CompanyId.HasValue && c.CompanyId.Value != userCompanyId.Value)
            return new ConsumableResult(false, "Consumable not found.", "NOT_FOUND");

        if (c.Status == ConsumableStatus.Confirmed)
            return new ConsumableResult(false, "Không thể xóa vật tư đã được xác nhận.");
        // Delete guard: a consumable that has ever been checked out must keep its allocation history
        // (consumable_checkouts FK is CASCADE — hard-deleting would wipe the whole history).
        var hasCheckouts = await _context.ConsumableCheckouts.AnyAsync(ch => ch.ConsumableId == request.Id, cancellationToken);
        if (hasCheckouts)
            return new ConsumableResult(false, "Vật tư đã từng được cấp phát, không thể xóa (lịch sử cấp phát phải được giữ).", "CONSUMABLE_HAS_CHECKOUTS");

        _context.Consumables.Remove(c);
        _actionLogService.LogAction(
            itemType: ItemType.Consumable,
            itemId: request.Id,
            actionType: ActionType.Delete,
            loggedByUserId: request.CurrentUserId,
            note: $"Deleted consumable: {c.Name}",
            companyId: c.CompanyId);
        await _context.SaveChangesAsync(cancellationToken);
        return new ConsumableResult(true, "Consumable deleted.");
    }
}
