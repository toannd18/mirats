using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Common.Sorting;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Accessories.Queries;

/// <summary>
/// [FIX-N2 2026-10-02] GET /api/v1/accessories — moved out of AccessoriesController (the last
/// controller still running EF queries directly). Logic is a VERBATIM move: filters (search/
/// categoryId/locationId), company scope, ordering, remaining/checkedOutQty/isLowStock math and
/// pagination shape are unchanged; only the place it lives changed.
/// [ACCESSORY-SORT 2026-10-21] Optional SortBy/SortDir added, mirroring the Asset list
/// (AssetListSort): DB-backed whitelist only, newest-created stays the default.
/// </summary>
public record ListAccessoriesQuery(
    string? Search,
    Guid? CategoryId,
    Guid? LocationId,
    int Page,
    int PageSize,
    string? SortBy = null,
    string? SortDir = null) : IRequest<ListAccessoriesResult>;

public sealed record AccessoryCategoryRefDto(Guid Id, string Name);

public sealed record AccessoryLocationRefDto(Guid Id, string Name);

public sealed record AccessoryManufacturerRefDto(Guid Id, string Name);

public sealed record AccessorySupplierRefDto(Guid Id, string Name);

/// <summary>
/// List row shape. ModelNumber/Manufacturer/Supplier are entity-backed (Accessory.ModelNumber,
/// ManufacturerId, SupplierId) — they exist on the entity, so the optional UI columns render real
/// values instead of a permanent "-". There is NO "Model" navigation on Accessory (only the free-text
/// ModelNumber), so no model ref is exposed here.
/// </summary>
public sealed record AccessoryListItemDto(
    Guid Id,
    string Name,
    string? ItemNo,
    string? ModelNumber,
    string? Notes,
    int Qty,
    int MinAmt,
    Guid? CompanyId,
    string? CompanyName,
    int Remaining,
    int CheckedOutQty,
    bool IsLowStock,
    AccessoryCategoryRefDto? Category,
    AccessoryLocationRefDto? Location,
    AccessoryManufacturerRefDto? Manufacturer,
    AccessorySupplierRefDto? Supplier);

public sealed record ListAccessoriesResult(IReadOnlyList<AccessoryListItemDto> Items, int Total);

/// <summary>
/// [ACCESSORY-SORT 2026-10-21] Whitelist sort for the Accessory list — DB-backed columns only.
/// ItemNo uses the shared PostgreSQL ICU numeric collation (und-u-kn-true) via NaturalSort.Collation
/// so "PK-2" sorts before "PK-10". Computed columns (checkedOutQty / remaining / isLowStock) are
/// deliberately NOT sortable: they are SUM() aggregates over AccessoryCheckout, not columns.
/// Unknown/absent SortBy falls back to newest-created first (then Name for a stable page order).
/// </summary>
public static class AccessoryListSort
{
    public static IOrderedQueryable<Accessory> Apply(IQueryable<Accessory> query, string? sortBy, bool desc)
    {
        var key = sortBy?.Trim().ToLowerInvariant();
        return key switch
        {
            "name" => desc ? query.OrderByDescending(a => a.Name) : query.OrderBy(a => a.Name),
            "itemno" => desc
                ? query.OrderByDescending(a => EF.Functions.Collate(a.ItemNo ?? string.Empty, NaturalSort.Collation))
                : query.OrderBy(a => EF.Functions.Collate(a.ItemNo ?? string.Empty, NaturalSort.Collation)),
            "qty" => desc ? query.OrderByDescending(a => a.Qty) : query.OrderBy(a => a.Qty),
            "minamt" => desc ? query.OrderByDescending(a => a.MinAmt) : query.OrderBy(a => a.MinAmt),
            _ => query.OrderByDescending(a => a.CreatedAt).ThenBy(a => a.Name),
        };
    }
}

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
            .Include(a => a.Location).Include(a => a.Company)
            .Include(a => a.Manufacturer).Include(a => a.Supplier).AsNoTracking();

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

        // [ACCESSORY-SORT 2026-10-21] Whitelist sort — unknown values fall back to newest-created.
        var desc = string.Equals(request.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        query = AccessoryListSort.Apply(query, request.SortBy, desc);

        var items = await query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(a => new AccessoryListItemDto(
                a.Id,
                a.Name,
                a.ItemNo,
                a.ModelNumber,
                a.Notes,
                a.Qty,
                a.MinAmt,
                a.CompanyId,
                a.Company != null ? a.Company.Name : null,
                a.Qty - a.Checkouts.Sum(ch => ch.AssignedQty - ch.ReturnedQty),
                a.Checkouts.Sum(ch => ch.AssignedQty - ch.ReturnedQty),
                (a.Qty - a.Checkouts.Sum(ch => ch.AssignedQty - ch.ReturnedQty)) <= a.MinAmt,
                a.Category == null ? null : new AccessoryCategoryRefDto(a.Category.Id, a.Category.Name),
                a.Location == null ? null : new AccessoryLocationRefDto(a.Location.Id, a.Location.Name),
                a.Manufacturer == null ? null : new AccessoryManufacturerRefDto(a.Manufacturer.Id, a.Manufacturer.Name),
                a.Supplier == null ? null : new AccessorySupplierRefDto(a.Supplier.Id, a.Supplier.Name)))
            .ToListAsync(cancellationToken);

        return new ListAccessoriesResult(items, total);
    }
}
