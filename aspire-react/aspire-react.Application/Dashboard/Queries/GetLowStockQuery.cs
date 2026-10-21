using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Common.Inventory;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Dashboard.Queries;

public record LowStockItemDto(Guid Id, string Name, int Qty, int MinAmt, int Remaining, string Type);

/// <summary>
/// [Giai đoạn 2-cuối] GET /api/v1/dashboard/low-stock (extracted verbatim from
/// DashboardController.GetLowStock). Company-scoped; 3 batched queries (Consumables/
/// Accessories/Components) with remaining-qty <= MinAmt filters, Take(10) each, concatenated
/// with Type discriminator — verbatim.
/// </summary>
public record GetLowStockQuery(Guid? CompanyId) : IRequest<IReadOnlyList<LowStockItemDto>>;

public class GetLowStockQueryHandler : IRequestHandler<GetLowStockQuery, IReadOnlyList<LowStockItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetLowStockQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<IReadOnlyList<LowStockItemDto>> Handle(GetLowStockQuery request, CancellationToken cancellationToken)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var consumableRows = await _context.Consumables
            .Include(c => c.Checkouts).Include(c => c.SparePartMovements).AsNoTracking()
            .Where(c => userCompanyId == null || c.CompanyId == null || c.CompanyId == userCompanyId.Value)
            .ToListAsync(cancellationToken);
        var consumables = consumableRows
            .Select(c => new { c, Remaining = InventoryRemaining.ForConsumable(c) })
            .Where(x => x.Remaining <= x.c.MinAmt)
            .Select(x => new LowStockItemDto(x.c.Id, x.c.Name, x.c.Qty, x.c.MinAmt, x.Remaining, "Consumable"))
            .Take(10).ToList();

        var accessories = await _context.Accessories.Include(a => a.Checkouts).AsNoTracking()
            .Where(a => (userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value)
                        && (a.Qty - a.Checkouts.Sum(ch => (int?)(ch.AssignedQty - ch.ReturnedQty) ?? 0)) <= a.MinAmt)
            .Select(a => new LowStockItemDto(a.Id, a.Name, a.Qty, a.MinAmt, a.Qty - a.Checkouts.Sum(ch => (int?)(ch.AssignedQty - ch.ReturnedQty) ?? 0), "Accessory"))
            .Take(10).ToListAsync(cancellationToken);

        var componentRows = await _context.Components
            .Include(c => c.Units).Include(c => c.Assignments).Include(c => c.SparePartMovements).AsNoTracking()
            .Where(c => userCompanyId == null || c.CompanyId == null || c.CompanyId == userCompanyId.Value)
            .ToListAsync(cancellationToken);
        var components = componentRows
            .Select(c => new { c, Remaining = InventoryRemaining.ForComponent(c) })
            .Where(x => x.Remaining <= x.c.MinAmt)
            .Select(x => new LowStockItemDto(x.c.Id, x.c.Name, x.c.Qty, x.c.MinAmt, x.Remaining, "Component"))
            .Take(10).ToList();

        var all = consumables.Concat(accessories).Concat(components).ToList();
        return all;
    }
}
