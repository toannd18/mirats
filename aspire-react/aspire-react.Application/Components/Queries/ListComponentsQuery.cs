using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Components.Queries;

public record ComponentListItemDto(
    Guid Id, string Name, string? Serial, int Qty, int MinAmt, string? ModelNumber,
    string? OrderNumber, decimal? PurchaseCost, DateTime? PurchaseDate, string? Notes,
    string TrackingType, int Remaining, bool IsLowStock, bool CanDelete,
    ComponentRefDto? Category, ComponentRefDto? Company, ComponentRefDto? Location,
    ComponentRefDto? Supplier, ComponentRefDto? Manufacturer);

public record ComponentListResult(IReadOnlyList<ComponentListItemDto> Items, int Total);

/// <summary>
/// [Giai đoạn 3 — Nặng] GET /api/v1/components (extracted from ComponentsController.GetComponents).
/// Verbatim: search/filters (uncategorized/uncompanied precedence), FMCS scoping, Remaining/
/// IsLowStock computed per TrackingType (Serial counts InStock units; Bulk qty-assignments),
/// canDelete via Checkout-log scan across component + its serial units (IgnoreQueryFilters on
/// units), pagination envelope built by the controller.
/// </summary>
public record ListComponentsQuery(
    string? Search, Guid? CategoryId, Guid? CompanyId, Guid? LocationId,
    bool Uncategorized = false, bool Uncompanied = false, int Page = 1, int PageSize = 20)
    : IRequest<ComponentListResult>;

public class ListComponentsQueryHandler : IRequestHandler<ListComponentsQuery, ComponentListResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public ListComponentsQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<ComponentListResult> Handle(ListComponentsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Components
            .Include(c => c.Assignments)
            .Include(c => c.Units)
            .Include(c => c.Category)
            .Include(c => c.Company)
            .Include(c => c.Location)
            .Include(c => c.Supplier)
            .Include(c => c.Manufacturer)
            .AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(s) || (c.Serial != null && c.Serial.ToLower().Contains(s)));
        }
        if (request.Uncategorized) query = query.Where(c => c.CategoryId == null);
        else if (request.CategoryId.HasValue) query = query.Where(c => c.CategoryId == request.CategoryId);
        if (request.Uncompanied) query = query.Where(c => c.CompanyId == null);
        else if (request.CompanyId.HasValue) query = query.Where(c => c.CompanyId == request.CompanyId);
        if (request.LocationId.HasValue) query = query.Where(c => c.LocationId == request.LocationId);

        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        query = query.Where(c => userCompanyId == null || c.CompanyId == null || c.CompanyId == userCompanyId.Value);

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(c => c.Name).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(c => new ComponentListItemDto(
                c.Id, c.Name, c.Serial, c.Qty, c.MinAmt, c.ModelNumber, c.OrderNumber,
                c.PurchaseCost, c.PurchaseDate, c.Notes,
                c.TrackingType.ToString(),
                c.TrackingType == TrackingType.Serial
                    ? c.Units.Count(u => u.Status == ComponentUnitStatus.InStock)
                    : c.Qty - c.Assignments.Sum(a => a.AssignedQty),
                (c.TrackingType == TrackingType.Serial
                    ? c.Units.Count(u => u.Status == ComponentUnitStatus.InStock)
                    : c.Qty - c.Assignments.Sum(a => a.AssignedQty)) <= c.MinAmt,
                false, // canDelete resolved below via checkout-log scan
                c.Category == null ? null : new ComponentRefDto(c.Category.Id, c.Category.Name),
                c.Company == null ? null : new ComponentRefDto(c.Company.Id, c.Company.Name),
                c.Location == null ? null : new ComponentRefDto(c.Location.Id, c.Location.Name),
                c.Supplier == null ? null : new ComponentRefDto(c.Supplier.Id, c.Supplier.Name),
                c.Manufacturer == null ? null : new ComponentRefDto(c.Manufacturer.Id, c.Manufacturer.Name)))
            .ToListAsync(cancellationToken);

        // canDelete = the component (or any of its serial units) has NEVER been checked out.
        var pageIds = items.Select(i => i.Id).ToList();
        var unitToComponent = await _context.ComponentUnits.IgnoreQueryFilters()
            .Where(u => pageIds.Contains(u.ComponentId))
            .Select(u => new { u.Id, u.ComponentId })
            .ToDictionaryAsync(u => u.Id, u => u.ComponentId, cancellationToken);
        var hasHistory = new HashSet<Guid>();
        if (pageIds.Count > 0)
        {
            var checkoutLogs = await _context.ActionLogs.AsNoTracking()
                .Where(l => l.ActionType == ActionType.Checkout &&
                    ((l.ItemType == ItemType.Component && pageIds.Contains(l.ItemId)) ||
                     (l.ItemType == ItemType.ComponentUnit && unitToComponent.Keys.Contains(l.ItemId))))
                .Select(l => new { l.ItemType, l.ItemId })
                .ToListAsync(cancellationToken);
            foreach (var log in checkoutLogs)
            {
                if (log.ItemType == ItemType.Component) hasHistory.Add(log.ItemId);
                else if (unitToComponent.TryGetValue(log.ItemId, out var cid)) hasHistory.Add(cid);
            }
        }

        var result = items.Select(c => c with { CanDelete = !hasHistory.Contains(c.Id) }).ToList();
        return new ComponentListResult(result, total);
    }
}
