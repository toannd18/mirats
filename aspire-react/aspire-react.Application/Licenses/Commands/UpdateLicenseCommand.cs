using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Licenses;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Licenses.Commands;

/// <summary>
/// [Giai đoạn 3 — Nặng] PUT /api/v1/licenses/{id} (extracted from LicensesController.Update).
/// Verbatim: FIELD_LOCKED category/company; seat-count sync (increase → generate seats 1..N;
/// decrease → CANNOT_REDUCE_SEATS_IN_USE if free &lt; reduction, remove highest free seats);
/// patch semantics ×12; TerminationDate Kind=Unspecified; thin Log in same SaveChanges.
/// </summary>
public record UpdateLicenseCommand(
    Guid Id, string? Name, string? Serial, int? Seats, bool? Reassignable,
    DateTime? ExpirationDate, DateTime? TerminationDate, decimal? PurchaseCost, DateTime? PurchaseDate,
    string? OrderNumber, int? MinSeats, string? Notes, Guid? SupplierId, Guid? ManufacturerId,
    Guid? CategoryId, Guid? CompanyId, Guid CurrentUserId) : IRequest<LicenseResult>;

public class UpdateLicenseCommandHandler : IRequestHandler<UpdateLicenseCommand, LicenseResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public UpdateLicenseCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<LicenseResult> Handle(UpdateLicenseCommand request, CancellationToken cancellationToken)
    {
        var l = await _context.Licenses.Include(x => x.LicenseSeats).FirstOrDefaultAsync(x => x.Id == request.Id && x.DeletedAt == null, cancellationToken);
        if (l == null)
            return new LicenseResult(false, "License not found.", "NOT_FOUND");
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (!LicenseRules.IsLicenseVisible(l, userCompanyId))
            return new LicenseResult(false, "License not found.", "NOT_FOUND");

        // Locked structural fields (same whitelist principle as Component/Maintenance).
        if (request.CategoryId.HasValue && request.CategoryId.Value != l.CategoryId)
            return new LicenseResult(false, "Không thể đổi danh mục sau khi tạo.", "FIELD_LOCKED");
        if (request.CompanyId.HasValue && request.CompanyId != l.CompanyId)
            return new LicenseResult(false, "Không thể đổi công ty sau khi tạo.", "FIELD_LOCKED");

        // Seat count sync: increase → generate new seats; decrease → only if enough free seats.
        if (request.Seats.HasValue && request.Seats.Value != l.Seats)
        {
            if (request.Seats.Value < l.Seats)
            {
                var free = l.LicenseSeats.Count(s => LicenseRules.CountTargets(s) == 0);
                if (free < (l.Seats - request.Seats.Value))
                    return new LicenseResult(false, "Không thể giảm số chỗ vì các chỗ đang được sử dụng.", "CANNOT_REDUCE_SEATS_IN_USE");
                var toRemove = l.LicenseSeats.Where(s => LicenseRules.CountTargets(s) == 0)
                    .OrderByDescending(s => s.SeatNumber).Take(l.Seats - request.Seats.Value).ToList();
                _context.LicenseSeats.RemoveRange(toRemove);
            }
            else
            {
                for (var i = l.Seats + 1; i <= request.Seats.Value; i++)
                    _context.LicenseSeats.Add(new LicenseSeat { LicenseId = l.Id, SeatNumber = i });
            }
            l.Seats = request.Seats.Value;
        }

        // ─── Patch semantics (Task M1, mirroring Task F Asset): only fields EXPLICITLY sent
        // (non-null / HasValue) are applied. A partial payload must NOT wipe the other fields.
        // CompanyId/CategoryId stay locked (rejected above); seats handled above.
        if (!string.IsNullOrWhiteSpace(request.Name)) l.Name = request.Name.Trim();
        l.Serial = request.Serial ?? l.Serial;
        if (request.Reassignable.HasValue) l.Reassignable = request.Reassignable.Value;
        if (request.ExpirationDate is not null) l.ExpirationDate = request.ExpirationDate;
        if (request.TerminationDate.HasValue) l.TerminationDate = DateTime.SpecifyKind(request.TerminationDate.Value, DateTimeKind.Unspecified);
        if (request.PurchaseCost is not null) l.PurchaseCost = request.PurchaseCost;
        if (request.PurchaseDate is not null) l.PurchaseDate = request.PurchaseDate;
        if (request.OrderNumber is not null) l.OrderNumber = request.OrderNumber;
        l.Notes = request.Notes ?? l.Notes;
        if (request.MinSeats.HasValue) l.MinSeats = request.MinSeats.Value;
        if (request.SupplierId is not null) l.SupplierId = request.SupplierId;
        if (request.ManufacturerId is not null) l.ManufacturerId = request.ManufacturerId;
        l.UpdatedAt = DateTime.UtcNow;

        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.License,
            ItemId = request.Id,
            ActionType = ActionType.Update,
            CreatedBy = request.CurrentUserId,
            CompanyId = l.CompanyId,
            Note = $"Cập nhật license \"{l.Name}\""
        });
        await _context.SaveChangesAsync(cancellationToken);
        return new LicenseResult(true, "License updated.");
    }
}
