using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Accessories.Queries;

/// <summary>
/// [FIX-N2 2026-10-02] GET /api/v1/accessories — moved out of AccessoriesController (the last
/// controller still running EF queries directly). Logic is a VERBATIM move: filters (search/
/// categoryId/locationId), company scope, ordering, remaining/checkedOutQty/isLowStock math and
/// pagination shape are unchanged; only the place it lives changed.
/// </summary>
public record ListAccessoriesQuery(
    string? Search,
    Guid? CategoryId,
    Guid? LocationId,
    int Page,
    int PageSize) : IRequest<ListAccessoriesResult>;

public sealed record AccessoryCategoryRefDto(Guid Id, string Name);

public sealed record AccessoryLocationRefDto(Guid Id, string Name);

public sealed record AccessoryListItemDto(
    Guid Id,
    string Name,
    string? ItemNo,
    string? Notes,
    int Qty,
    int MinAmt,
    Guid? CompanyId,
    string? CompanyName,
    int Remaining,
    int CheckedOutQty,
    bool IsLowStock,
    AccessoryCategoryRefDto? Category,
    AccessoryLocationRefDto? Location);

public sealed record ListAccessoriesResult(IReadOnlyList<AccessoryListItemDto> Items, int Total);

public class ListAccessoriesQueryHandler : IRequestHandler<ListAccessoriesQuery, ListAccessoriesResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public ListAccessoriesQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<ListAccessoriesResult> Handle(ListAccessoriesQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Accessories.Include(a => a.Checkouts).Include(a => a.Category)
            .Include(a => a.Location).Include(a => a.Company).AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.ToLower();
            query = query.Where(a => a.Name.ToLower().Contains(s) || (a.ItemNo != null && a.ItemNo.ToLower().Contains(s)));
        }
        if (request.CategoryId.HasValue) query = query.Where(a => a.CategoryId == request.CategoryId);
        if (request.LocationId.HasValue) query = query.Where(a => a.LocationId == request.LocationId);

        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        query = query.Where(a => userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value);

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(a => a.Name).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(a => new AccessoryListItemDto(
                a.Id,
                a.Name,
                a.ItemNo,
                a.Notes,
                a.Qty,
                a.MinAmt,
                a.CompanyId,
                a.Company != null ? a.Company.Name : null,
                a.Qty - a.Checkouts.Sum(ch => ch.AssignedQty - ch.ReturnedQty),
                a.Checkouts.Sum(ch => ch.AssignedQty - ch.ReturnedQty),
                (a.Qty - a.Checkouts.Sum(ch => ch.AssignedQty - ch.ReturnedQty)) <= a.MinAmt,
                a.Category == null ? null : new AccessoryCategoryRefDto(a.Category.Id, a.Category.Name),
                a.Location == null ? null : new AccessoryLocationRefDto(a.Location.Id, a.Location.Name)))
            .ToListAsync(cancellationToken);

        return new ListAccessoriesResult(items, total);
    }
}
