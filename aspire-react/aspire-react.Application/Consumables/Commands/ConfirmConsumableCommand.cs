using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;

namespace aspire_react.Server.Application.Consumables.Commands;

/// <summary>
/// [Giai đoạn 3 — Nặng] PUT /api/v1/consumables/{id}/confirm (extracted from
/// ConsumablesController.Confirm). Verbatim: scope → 404; already-confirmed → 400;
/// status flip + Confirm ActionLog.
/// </summary>
public record ConfirmConsumableCommand(Guid Id, Guid CurrentUserId) : IRequest<ConsumableResult>;

public class ConfirmConsumableCommandHandler : IRequestHandler<ConfirmConsumableCommand, ConsumableResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public ConfirmConsumableCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<ConsumableResult> Handle(ConfirmConsumableCommand request, CancellationToken cancellationToken)
    {
        // [Task K] Company-scoping: only a user of the consumable's company may confirm it.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var c = await _context.Consumables.FindAsync(new object?[] { request.Id }, cancellationToken);
        if (c == null || (userCompanyId.HasValue && c.CompanyId.HasValue && c.CompanyId.Value != userCompanyId.Value))
            return new ConsumableResult(false, "Consumable not found.", "NOT_FOUND");
        if (c.Status == ConsumableStatus.Confirmed)
            return new ConsumableResult(false, "Vật tư đã được xác nhận.");
        c.Status = ConsumableStatus.Confirmed;
        _actionLogService.LogAction(
            itemType: ItemType.Consumable,
            itemId: request.Id,
            actionType: ActionType.Confirm,
            loggedByUserId: request.CurrentUserId,
            note: "Consumable confirmed.",
            companyId: c.CompanyId);
        await _context.SaveChangesAsync(cancellationToken);
        return new ConsumableResult(true, "Consumable confirmed.");
    }
}
