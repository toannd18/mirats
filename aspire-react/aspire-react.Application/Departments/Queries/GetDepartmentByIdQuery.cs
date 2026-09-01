using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Departments.Queries;

/// <summary>
/// [Giai đoạn 1 — pilot MediatR] GET /api/v1/departments/{id}. Returns the Department entity
/// (same shape the pre-migration controller serialized) or NULL when missing / out of company
/// scope — the controller maps null to the exact same 404 body as before (Task K: hide existence).
/// </summary>
public record GetDepartmentByIdQuery(Guid Id) : IRequest<Department?>;

public class GetDepartmentByIdQueryHandler : IRequestHandler<GetDepartmentByIdQuery, Department?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetDepartmentByIdQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<Department?> Handle(GetDepartmentByIdQuery request, CancellationToken cancellationToken)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var d = await _context.Departments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (d == null || (userCompanyId.HasValue && d.CompanyId.HasValue && d.CompanyId.Value != userCompanyId.Value))
            return null;
        return d;
    }
}
