using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Consumables.Queries;

public record ConsumableRefDto(Guid Id, string Name);

public record ConsumableListItemDto(
    Guid Id, string Name, string? ItemNo, string? Notes, int Qty, int MinAmt, string Status,
    Guid? CompanyId, string? CompanyName, int Remaining, bool IsLowStock,
    ConsumableRefDto? Category, ConsumableRefDto? Location);

public record ConsumableListResult(IReadOnlyList<ConsumableListItemDto> Items, int Total);

/// <summary>
/// [Giai đoạn 3 — Nặng] GET /api/v1/consumables (extracted from ConsumablesController.GetConsumables).
/// Verbatim: search/filters, FMCS scoping, Remaining/IsLowStock computed from Checkouts sum.
/// </summary>
public record ListConsumablesQuery(string? Search, Guid? CategoryId, Guid? LocationId, int Page = 1, int PageSize = 20)
    : IRequest<ConsumableListResult>;

public class ListConsumablesQueryHandler : IRequestHandler<ListConsumablesQuery, ConsumableListResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public ListConsumablesQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<ConsumableListResult> Handle(ListConsumablesQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Consumables.Include(c => c.Checkouts).Include(c => c.Category)
            .Include(c => c.Location).Include(c => c.Company).AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(s) || (c.ItemNo != null && c.ItemNo.ToLower().Contains(s)));
        }
        if (request.CategoryId.HasValue) query = query.Where(c => c.CategoryId == request.CategoryId);
        if (request.LocationId.HasValue) query = query.Where(c => c.LocationId == request.LocationId);

        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        query = query.Where(c => userCompanyId == null || c.CompanyId == null || c.CompanyId == userCompanyId.Value);

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(c => c.Name).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(c => new ConsumableListItemDto(
                c.Id, c.Name, c.ItemNo, c.Notes, c.Qty, c.MinAmt, c.Status.ToString(), c.CompanyId,
                c.Company != null ? c.Company.Name : null,
                c.Qty - c.Checkouts.Sum(ch => ch.Quantity),
                (c.Qty - c.Checkouts.Sum(ch => ch.Quantity)) <= c.MinAmt,
                c.Category == null ? null : new ConsumableRefDto(c.Category.Id, c.Category.Name),
                c.Location == null ? null : new ConsumableRefDto(c.Location.Id, c.Location.Name)))
            .ToListAsync(cancellationToken);

        return new ConsumableListResult(items, total);
    }
}
