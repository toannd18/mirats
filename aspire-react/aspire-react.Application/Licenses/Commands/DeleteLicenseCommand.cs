using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Licenses;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Licenses.Commands;

/// <summary>
/// [Giai đoạn 3 — Nặng] DELETE /api/v1/licenses/{id} (extracted from LicensesController.Delete).
/// Verbatim: SOFT-delete (DeletedAt Kind=Unspecified + UpdatedAt UTC — the safe-list convention);
/// LICENSE_IN_USE when any seat assigned or any Checkout log exists; thin Log in same SaveChanges.
/// </summary>
public record DeleteLicenseCommand(Guid Id, Guid CurrentUserId) : IRequest<LicenseResult>;

public class DeleteLicenseCommandHandler : IRequestHandler<DeleteLicenseCommand, LicenseResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public DeleteLicenseCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<LicenseResult> Handle(DeleteLicenseCommand request, CancellationToken cancellationToken)
    {
        var l = await _context.Licenses.Include(x => x.LicenseSeats).FirstOrDefaultAsync(x => x.Id == request.Id && x.DeletedAt == null, cancellationToken);
        if (l == null)
            return new LicenseResult(false, "License not found.", "NOT_FOUND");
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (!LicenseRules.IsLicenseVisible(l, userCompanyId))
            return new LicenseResult(false, "License not found.", "NOT_FOUND");

        var assigned = l.LicenseSeats.Count(s => LicenseRules.CountTargets(s) > 0);
        var anyCheckout = await _context.ActionLogs.AnyAsync(a => a.ItemType == ItemType.License && a.ItemId == request.Id && a.ActionType == ActionType.Checkout, cancellationToken);
        if (assigned > 0 || anyCheckout)
            return new LicenseResult(false, "Không thể xóa license vì đã có seat được cấp phát.", "LICENSE_IN_USE");

        l.DeletedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        // l.UpdatedAt is `timestamp with time zone` (safe list) → keep Kind=UTC.
        l.UpdatedAt = DateTime.UtcNow;
        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.License,
            ItemId = request.Id,
            ActionType = ActionType.Delete,
            CreatedBy = request.CurrentUserId,
            CompanyId = l.CompanyId,
            Note = $"Xóa license \"{l.Name}\""
        });
        await _context.SaveChangesAsync(cancellationToken);
        return new LicenseResult(true, "License deleted.");
    }
}
