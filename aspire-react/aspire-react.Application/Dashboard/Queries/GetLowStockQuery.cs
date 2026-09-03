using aspire_react.Server.Application.Common.Interfaces;
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
        var consumables = await _context.Consumables.Include(c => c.Checkouts).AsNoTracking()
            .Where(c => (userCompanyId == null || c.CompanyId == null || c.CompanyId == userCompanyId.Value)
                        && (c.Qty - c.Checkouts.Sum(ch => (int?)ch.Quantity ?? 0)) <= c.MinAmt)
            .Select(c => new LowStockItemDto(c.Id, c.Name, c.Qty, c.MinAmt, c.Qty - c.Checkouts.Sum(ch => (int?)ch.Quantity ?? 0), "Consumable"))
            .Take(10).ToListAsync(cancellationToken);

        var accessories = await _context.Accessories.Include(a => a.Checkouts).AsNoTracking()
            .Where(a => (userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value)
                        && (a.Qty - a.Checkouts.Sum(ch => (int?)(ch.AssignedQty - ch.ReturnedQty) ?? 0)) <= a.MinAmt)
            .Select(a => new LowStockItemDto(a.Id, a.Name, a.Qty, a.MinAmt, a.Qty - a.Checkouts.Sum(ch => (int?)(ch.AssignedQty - ch.ReturnedQty) ?? 0), "Accessory"))
            .Take(10).ToListAsync(cancellationToken);

        var components = await _context.Components.Include(c => c.Assignments).AsNoTracking()
            .Where(c => (userCompanyId == null || c.CompanyId == null || c.CompanyId == userCompanyId.Value)
                        && (c.Qty - c.Assignments.Sum(a => (int?)a.AssignedQty ?? 0)) <= c.MinAmt)
            .Select(c => new LowStockItemDto(c.Id, c.Name, c.Qty, c.MinAmt, c.Qty - c.Assignments.Sum(a => (int?)a.AssignedQty ?? 0), "Component"))
            .Take(10).ToListAsync(cancellationToken);

        var all = consumables.Concat(accessories).Concat(components).ToList();
        return all;
    }
}
