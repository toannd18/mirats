using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Departments.Queries;

/// <summary>Projection DTO — property names/shapes mirror the pre-migration anonymous projection exactly.</summary>
public record DepartmentCompanyDto(Guid Id, string Name);

public record DepartmentManagerDto(Guid Id, string Username, string FirstName, string LastName);

public record DepartmentListItemDto(
    Guid Id,
    string Name,
    string? Phone,
    string? Fax,
    Guid? CompanyId,
    DepartmentCompanyDto? Company,
    DepartmentManagerDto? Manager);

/// <summary>
/// [Giai đoạn 1 — pilot MediatR] GET /api/v1/departments. Company-scoping moved verbatim from
/// DepartmentsController.GetAll (Task K): a regular user is FORCED to their own company scope
/// (or floater) — the optional companyId query param is ignored for scoping; superuser may
/// optionally filter by companyId.
/// </summary>
public record ListDepartmentsQuery(Guid? CompanyId) : IRequest<IReadOnlyList<DepartmentListItemDto>>;

public class ListDepartmentsQueryHandler : IRequestHandler<ListDepartmentsQuery, IReadOnlyList<DepartmentListItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public ListDepartmentsQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<IReadOnlyList<DepartmentListItemDto>> Handle(ListDepartmentsQuery request, CancellationToken cancellationToken)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var query = _context.Departments
            .Include(d => d.Company)
            .Include(d => d.Manager)
            .AsNoTracking()
            .AsQueryable();

        if (userCompanyId.HasValue)
            query = query.Where(d => d.CompanyId == null || d.CompanyId == userCompanyId.Value);
        else if (request.CompanyId.HasValue)
            query = query.Where(d => d.CompanyId == request.CompanyId.Value);

        var list = await query
            .OrderBy(d => d.Name)
            .Select(d => new DepartmentListItemDto(
                d.Id,
                d.Name,
                d.Phone,
                d.Fax,
                d.CompanyId,
                d.Company == null ? null : new DepartmentCompanyDto(d.Company.Id, d.Company.Name),
                d.Manager == null ? null : new DepartmentManagerDto(d.Manager.Id, d.Manager.Username, d.Manager.FirstName, d.Manager.LastName)))
            .ToListAsync(cancellationToken);
        return list;
    }
}
