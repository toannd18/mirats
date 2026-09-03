using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Components.Commands;

/// <summary>
/// [Giai đoạn 3 — Nặng] DELETE /api/v1/components/{id} (extracted from ComponentsController.Delete).
/// Verbatim: scope → 404; allocation-history guard (component-level OR any serial unit Checkout log
/// → COMPONENT_HAS_ALLOCATION_HISTORY); log BEFORE removal (audit trail retains the name) via
/// IActionLogService (same SaveChanges as removal — NOT ILoggableCommand, verbatim ordering).
/// </summary>
public record DeleteComponentCommand(Guid Id, Guid CurrentUserId) : IRequest<ComponentOperationResult>;

public class DeleteComponentCommandHandler : IRequestHandler<DeleteComponentCommand, ComponentOperationResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public DeleteComponentCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<ComponentOperationResult> Handle(DeleteComponentCommand request, CancellationToken cancellationToken)
    {
        var c = await _context.Components.Include(x => x.Units).FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (c == null)
            return new ComponentOperationResult(false, "Component not found.", "NOT_FOUND");

        // Company scoping: a regular user may only delete components of their own company (or floater).
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && c.CompanyId.HasValue && c.CompanyId.Value != userCompanyId.Value)
            return new ComponentOperationResult(false, "Component not found.", "NOT_FOUND");

        // ─── Delete guard ───
        // A component that has EVER been checked out (Component-level or any of its serial units)
        // cannot be deleted — the ActionLog audit trail must stay intact. Even if everything has
        // been checked back in, history is preserved for auditing.
        var unitIds = c.Units.Select(u => u.Id).ToList();
        var hasAllocationHistory =
            await _context.ActionLogs.AsNoTracking().AnyAsync(l => l.ActionType == ActionType.Checkout &&
                ((l.ItemType == ItemType.Component && l.ItemId == request.Id) ||
                 (l.ItemType == ItemType.ComponentUnit && unitIds.Contains(l.ItemId))), cancellationToken);
        if (hasAllocationHistory)
            return new ComponentOperationResult(false, "Linh kiện đã từng được cấp phát, không thể xóa.", "COMPONENT_HAS_ALLOCATION_HISTORY");

        _actionLogService.Log(new ActionLogEntry { ItemType = ItemType.Component, ItemId = request.Id, ActionType = ActionType.Delete, CreatedBy = request.CurrentUserId, CompanyId = c.CompanyId, Note = $"Xóa linh kiện \"{c.Name}\"" });
        _context.Components.Remove(c);
        await _context.SaveChangesAsync(cancellationToken);
        return new ComponentOperationResult(true, "Component deleted.");
    }
}
