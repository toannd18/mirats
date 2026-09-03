using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Licenses;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Licenses.Queries;

/// <summary>[Giai đoạn 3 — Nặng] GET /api/v1/licenses/{id}/seats (extracted from
/// LicensesController.GetSeats — shares the seat projection). NULL → 404.</summary>
public record GetLicenseSeatsQuery(Guid Id) : IRequest<IReadOnlyList<LicenseSeatDto>?>;

public class GetLicenseSeatsQueryHandler : IRequestHandler<GetLicenseSeatsQuery, IReadOnlyList<LicenseSeatDto>?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetLicenseSeatsQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<IReadOnlyList<LicenseSeatDto>?> Handle(GetLicenseSeatsQuery request, CancellationToken cancellationToken)
    {
        var l = await _context.Licenses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id && x.DeletedAt == null, cancellationToken);
        if (l == null) return null;
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (!LicenseRules.IsLicenseVisible(l, userCompanyId)) return null;

        var seats = await _context.LicenseSeats.AsNoTracking()
            .Include(s => s.User).Include(s => s.Asset).Include(s => s.SystemInfo)
            .Where(s => s.LicenseId == request.Id).OrderBy(s => s.SeatNumber).ToListAsync(cancellationToken);
        return LicenseSeatProjection.Project(seats);
    }
}
