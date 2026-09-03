using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Companies.Queries;

public record CompanyByIdDto(Guid Id, string Name, string? Code, Guid? ParentId);

/// <summary>
/// [Giai đoạn 3] GET /api/v1/companies/{id} (extracted from CompaniesController.Get — SEC-FIX S5).
/// Company-scoping verbatim: superuser → any company; regular user WITH a company → only companies
/// inside their subtree (own + descendants — a child user may NOT read a parent/other-branch
/// company by id); company-less regular user → still allowed to VIEW (consistent with GetAll).
/// Out-of-scope → NULL → controller 404 (hide existence). Response includes an empty Children
/// list (verbatim pre-migration shape).
/// </summary>
public record GetCompanyByIdQuery(Guid Id) : IRequest<CompanyByIdDto?>;

public class GetCompanyByIdQueryHandler : IRequestHandler<GetCompanyByIdQuery, CompanyByIdDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetCompanyByIdQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<CompanyByIdDto?> Handle(GetCompanyByIdQuery request, CancellationToken cancellationToken)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && userCompanyId.Value != Guid.Empty && !_companyScope.IsSuperUser()
            && !await _companyScope.IsCompanyIdInUserScopeAsync(request.Id))
            return null;

        var c = await _context.Companies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (c == null) return null;
        return new CompanyByIdDto(c.Id, c.Name, c.Code, c.ParentId);
    }
}
