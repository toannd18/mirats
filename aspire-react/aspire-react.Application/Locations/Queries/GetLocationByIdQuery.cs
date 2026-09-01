using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Locations.Queries;

/// <summary>
/// [Giai đoạn 2] GET /api/v1/locations/{id} — NEW endpoint (was missing pre-migration).
/// Company-scoping APPLIED per approved decision (matches GetAll/Update/Delete of this section —
/// NOT Create, which is the known BUG-G gap): out-of-scope → NULL → controller maps to 404
/// hide-existence. Returns the Location entity.
/// </summary>
public record GetLocationByIdQuery(Guid Id) : IRequest<Location?>;

public class GetLocationByIdQueryHandler : IRequestHandler<GetLocationByIdQuery, Location?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetLocationByIdQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<Location?> Handle(GetLocationByIdQuery request, CancellationToken cancellationToken)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var l = await _context.Locations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (l == null || (userCompanyId.HasValue && l.CompanyId.HasValue && l.CompanyId.Value != userCompanyId.Value))
            return null;
        return l;
    }
}
