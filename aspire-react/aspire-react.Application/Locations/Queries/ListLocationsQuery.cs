using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Locations.Queries;

public record LocationListItemDto(
    Guid Id,
    string Name,
    Guid? ParentId,
    Guid? CompanyId,
    Guid? ManagerId,
    string? Address,
    string? City,
    string? State,
    string? Country,
    string? Zip);

/// <summary>
/// [Giai đoạn 2] GET /api/v1/locations (extracted from AdminController.GetLocations).
/// Company-scoping verbatim (Task U): regular user FORCED to own-company/floater — the optional
/// companyId query param is ignored for scoping; superuser may optionally filter by companyId.
/// Projection verbatim (10 scalar fields, no navs). NOTE: no OutputCache on this endpoint
/// (pre-migration had none) — no ICacheInvalidatingCommand for Location commands.
/// </summary>
public record ListLocationsQuery(Guid? CompanyId) : IRequest<IReadOnlyList<LocationListItemDto>>;

public class ListLocationsQueryHandler : IRequestHandler<ListLocationsQuery, IReadOnlyList<LocationListItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public ListLocationsQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<IReadOnlyList<LocationListItemDto>> Handle(ListLocationsQuery request, CancellationToken cancellationToken)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var query = _context.Locations.AsNoTracking().AsQueryable();
        if (userCompanyId.HasValue)
            query = query.Where(l => l.CompanyId == null || l.CompanyId == userCompanyId.Value);
        else if (request.CompanyId.HasValue)
            query = query.Where(l => l.CompanyId == request.CompanyId.Value);
        var list = await query.OrderBy(l => l.Name)
            .Select(l => new LocationListItemDto(
                l.Id, l.Name, l.ParentId, l.CompanyId, l.ManagerId, l.Address, l.City, l.State, l.Country, l.Zip))
            .ToListAsync(cancellationToken);
        return list;
    }
}
