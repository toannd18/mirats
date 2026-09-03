using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Licenses;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Licenses.Commands;

/// <summary>
/// [Giai đoạn 3 — Nặng] POST /api/v1/licenses/{id}/checkin (extracted from LicensesController.
/// CheckinSeat). Verbatim: scope → 404; SEAT_NOT_FOUND / SEAT_NOT_ASSIGNED /
/// LICENSE_NOT_REASSIGNABLE; seat cleared + thin Log in same SaveChanges (no explicit tx —
/// verbatim).</summary>
public record CheckinLicenseSeatCommand(Guid LicenseId, Guid SeatId, Guid CurrentUserId)
    : IRequest<LicenseSeatActionResult>;

public class CheckinLicenseSeatCommandHandler : IRequestHandler<CheckinLicenseSeatCommand, LicenseSeatActionResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public CheckinLicenseSeatCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<LicenseSeatActionResult> Handle(CheckinLicenseSeatCommand request, CancellationToken cancellationToken)
    {
        var l = await _context.Licenses.FirstOrDefaultAsync(x => x.Id == request.LicenseId && x.DeletedAt == null, cancellationToken);
        if (l == null)
            return new LicenseSeatActionResult(false, "License not found.", "NOT_FOUND");
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (!LicenseRules.IsLicenseVisible(l, userCompanyId))
            return new LicenseSeatActionResult(false, "License not found.", "NOT_FOUND");

        var seat = await _context.LicenseSeats.FirstOrDefaultAsync(s => s.Id == request.SeatId && s.LicenseId == request.LicenseId, cancellationToken);
        if (seat == null)
            return new LicenseSeatActionResult(false, "Seat not found.", "SEAT_NOT_FOUND");
        if (LicenseRules.CountTargets(seat) == 0)
            return new LicenseSeatActionResult(false, "Seat này chưa được cấp phát.", "SEAT_NOT_ASSIGNED");

        if (!l.Reassignable)
            return new LicenseSeatActionResult(false, "License không cho phép thu hồi để cấp lại (Reassignable = false).", "LICENSE_NOT_REASSIGNABLE");

        seat.UserId = null; seat.AssetId = null; seat.SystemInfoId = null;
        seat.AssignedAt = null; seat.Note = null; seat.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.License,
            ItemId = request.LicenseId,
            ActionType = ActionType.Checkin,
            CreatedBy = request.CurrentUserId,
            CompanyId = l.CompanyId,
            Note = $"Thu hồi seat #{seat.SeatNumber}"
        });
        await _context.SaveChangesAsync(cancellationToken);
        return new LicenseSeatActionResult(true, "Seat checked in.");
    }
}
