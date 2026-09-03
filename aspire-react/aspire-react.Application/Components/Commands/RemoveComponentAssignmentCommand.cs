using System.Text.Json;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Components.Commands;

/// <summary>
/// [Giai đoạn 3 — Nặng] POST /api/v1/components/{id}/remove (extracted from
/// ComponentsController.RemoveAssignment — legacy quantity endpoint). Verbatim: scope → 404;
/// Serial components reject quantity assignment (use /checkin); log carries TargetType=Asset +
/// LogMeta quantity; single SaveChanges (log + removal atomic) via IActionLogService — no
/// explicit transaction (verbatim), no ILoggableCommand.
/// </summary>
public record RemoveComponentAssignmentCommand(Guid ComponentId, Guid AssignmentId, Guid CurrentUserId)
    : IRequest<ComponentOperationResult>;

public class RemoveComponentAssignmentCommandHandler : IRequestHandler<RemoveComponentAssignmentCommand, ComponentOperationResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public RemoveComponentAssignmentCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<ComponentOperationResult> Handle(RemoveComponentAssignmentCommand request, CancellationToken cancellationToken)
    {
        // [Task K] Company-scoping: only a user of the component's company may remove its assignment.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var a = await _context.ComponentAssignments.Include(ca => ca.Component)
            .FirstOrDefaultAsync(ca => ca.Id == request.AssignmentId && ca.ComponentId == request.ComponentId, cancellationToken);
        if (a == null || (userCompanyId.HasValue && a.Component.CompanyId.HasValue && a.Component.CompanyId.Value != userCompanyId.Value))
            return new ComponentOperationResult(false, "Assignment not found.", "NOT_FOUND");
        if (a.Component.TrackingType == TrackingType.Serial)
            return new ComponentOperationResult(false, "Linh kiện Serial không dùng assignment quantity — dùng /checkin.");

        _context.ComponentAssignments.Remove(a);

        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.Component,
            ItemId = request.ComponentId,
            TargetType = AssignmentTargetType.Asset,
            TargetId = a.AssetId,
            ActionType = ActionType.Checkin,
            CreatedBy = request.CurrentUserId,
            CompanyId = a.Component.CompanyId,
            LogMeta = JsonSerializer.Serialize(new { quantity = a.AssignedQty })
        });

        await _context.SaveChangesAsync(cancellationToken);
        return new ComponentOperationResult(true, "Component assignment removed.");
    }
}
