using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Companies.Queries;

public record CompanyTreeNodeDto(Guid Id, string Name, string? Code, Guid? ParentId, List<CompanyTreeNodeDto> Children);

/// <summary>
/// [Giai đoạn 3] GET /api/v1/companies (extracted from CompaniesController.GetAll — Task V).
/// Company-scoping verbatim: superuser → full tree; regular user WITH a company → only that
/// company's subtree; company-less regular user (Guid.Empty sentinel) → full tree (2026-08-23
/// decision: company-less users may VIEW the tree to be assigned a company).
/// A node is a root when its ParentId is null OR its parent is outside the visible set
/// (the scoped subtree root). NOTE: the [OutputCache] attribute stays on the controller action
/// (RefDataCompanyScope policy, ref:companies tag) — response caching is an HTTP concern.
/// </summary>
public record ListCompaniesQuery : IRequest<IReadOnlyList<CompanyTreeNodeDto>>;

public class ListCompaniesQueryHandler : IRequestHandler<ListCompaniesQuery, IReadOnlyList<CompanyTreeNodeDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public ListCompaniesQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<IReadOnlyList<CompanyTreeNodeDto>> Handle(ListCompaniesQuery request, CancellationToken cancellationToken)
    {
        // [Task V] Never trust a client-supplied param.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();

        List<Company> all;
        if (userCompanyId.HasValue && userCompanyId.Value != Guid.Empty && !_companyScope.IsSuperUser())
        {
            all = await GetSubtreeAsync(userCompanyId.Value, cancellationToken);
        }
        else
        {
            all = await _context.Companies.AsNoTracking().OrderBy(c => c.Name).ToListAsync(cancellationToken);
        }

        var roots = BuildTree(all, null);
        return roots;
    }

    private async Task<HashSet<Guid>> GetDescendantIdsAsync(Guid parentId, CancellationToken cancellationToken)
    {
        var result = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(parentId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var children = await _context.Companies.Where(c => c.ParentId == current).Select(c => c.Id).ToListAsync(cancellationToken);
            foreach (var childId in children)
            {
                if (result.Add(childId)) queue.Enqueue(childId);
            }
        }
        return result;
    }

    /// <summary>Returns the company subtree rooted at <paramref name="companyId"/> (the company + all descendants).</summary>
    private async Task<List<Company>> GetSubtreeAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var ids = new HashSet<Guid> { companyId };
        ids.UnionWith(await GetDescendantIdsAsync(companyId, cancellationToken));
        return await _context.Companies.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    private static List<CompanyTreeNodeDto> BuildTree(List<Company> all, Guid? parentId)
    {
        // A node is a root when its ParentId is null OR its parent is not in the visible set
        // (the subtree root of a scoped regular user — its parent is excluded by the scope filter).
        var visibleIds = new HashSet<Guid>(all.Select(c => c.Id));
        return all.Where(c => c.ParentId == parentId || (parentId == null && c.ParentId.HasValue && !visibleIds.Contains(c.ParentId.Value)))
            .Select(c => new CompanyTreeNodeDto(c.Id, c.Name, c.Code, c.ParentId, BuildTree(all, c.Id)))
            .ToList();
    }
}
