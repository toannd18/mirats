using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Common.Sorting;
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
public record ListConsumablesQuery(string? Search, Guid? CategoryId, Guid? LocationId, int Page = 1, int PageSize = 20,
    string? SortBy = null, string? SortDir = null)
    : IRequest<ConsumableListResult>;

/// <summary>
/// UI-1 — whitelist sort for the Consumable list. Only DB-backed scalars are sortable: Name,
/// ItemNo (natural ICU numeric collation so "VT2" sorts before "VT10"), Status (persisted enum)
/// and CreatedAt. Computed columns (Remaining / IsLowStock) are NEVER sorted here because they are
/// not DB scalars — an unknown/missing sort falls back to newest-created first.
/// </summary>
public static class ConsumableListSort
{
    public static IOrderedQueryable<Consumable> Apply(IQueryable<Consumable> query, string? sortBy, bool desc)
    {
        var key = sortBy?.Trim().ToLowerInvariant();
        return key switch
        {
            "name" => desc ? query.OrderByDescending(c => c.Name) : query.OrderBy(c => c.Name),
            "status" => desc ? query.OrderByDescending(c => c.Status) : query.OrderBy(c => c.Status),
            "itemno" => desc
                ? query.OrderByDescending(c => EF.Functions.Collate(c.ItemNo, NaturalSort.Collation))
                : query.OrderBy(c => EF.Functions.Collate(c.ItemNo, NaturalSort.Collation)),
            "createdat" => desc ? query.OrderByDescending(c => c.CreatedAt) : query.OrderBy(c => c.CreatedAt),
            _ => query.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Name),
        };
    }
}

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

        // UI-1 — whitelist sort; unknown/missing sortBy falls back to newest-created first.
        var desc = string.Equals(request.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        query = ConsumableListSort.Apply(query, request.SortBy, desc);

        var items = await query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
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
