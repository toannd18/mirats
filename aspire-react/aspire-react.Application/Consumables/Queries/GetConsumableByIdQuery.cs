using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Consumables.Queries;

public record ConsumableDetailDto(
    Guid Id, string Name, string? ItemNo, int Qty, int MinAmt, string Status,
    string? ModelNumber, string? OrderNumber, DateTime? PurchaseDate, decimal? PurchaseCost,
    string? Notes, Guid? CategoryId, Guid? ManufacturerId, Guid? SupplierId, Guid? LocationId,
    Guid? CompanyId, int Remaining, double PercentRemaining, bool IsLowStock,
    ConsumableRefDto? Category, ConsumableRefDto? Manufacturer, ConsumableRefDto? Supplier,
    ConsumableRefDto? Location, ConsumableRefDto? Company);

/// <summary>
/// [Giai đoạn 3 — Nặng] GET /api/v1/consumables/{id} (extracted from
/// ConsumablesController.GetConsumable). Scope → 404 AFTER the row lookup (verbatim order —
/// hides existence); PercentRemaining math verbatim; detail shape verbatim.
/// </summary>
public record GetConsumableByIdQuery(Guid Id) : IRequest<ConsumableDetailDto?>;

public class GetConsumableByIdQueryHandler : IRequestHandler<GetConsumableByIdQuery, ConsumableDetailDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetConsumableByIdQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<ConsumableDetailDto?> Handle(GetConsumableByIdQuery request, CancellationToken cancellationToken)
    {
        var c = await _context.Consumables.Include(x => x.Checkouts).Include(x => x.Category)
            .Include(x => x.Manufacturer).Include(x => x.Supplier).Include(x => x.Location)
            .Include(x => x.Company).AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (c == null) return null;

        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && c.CompanyId.HasValue && c.CompanyId.Value != userCompanyId.Value)
            return null;

        var remaining = c.Qty - c.Checkouts.Sum(ch => ch.Quantity);
        return new ConsumableDetailDto(
            c.Id, c.Name, c.ItemNo, c.Qty, c.MinAmt, c.Status.ToString(),
            c.ModelNumber, c.OrderNumber, c.PurchaseDate, c.PurchaseCost, c.Notes,
            c.CategoryId, c.ManufacturerId, c.SupplierId, c.LocationId, c.CompanyId,
            remaining,
            c.Qty > 0 ? Math.Round((double)remaining / c.Qty * 100, 2) : 0,
            remaining <= c.MinAmt,
            c.Category == null ? null : new ConsumableRefDto(c.Category.Id, c.Category.Name),
            c.Manufacturer == null ? null : new ConsumableRefDto(c.Manufacturer.Id, c.Manufacturer.Name),
            c.Supplier == null ? null : new ConsumableRefDto(c.Supplier.Id, c.Supplier.Name),
            c.Location == null ? null : new ConsumableRefDto(c.Location.Id, c.Location.Name),
            c.Company == null ? null : new ConsumableRefDto(c.Company.Id, c.Company.Name));
    }
}
