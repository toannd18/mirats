using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Common.Inventory;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Consumables.Queries;

public record ConsumableLowStockRowDto(Guid Id, string Name, string? ItemNo, int Qty, int MinAmt, int Remaining);

/// <summary>
/// [Giai đoạn 3 — Nặng] GET /api/v1/consumables/low-stock (extracted from
/// ConsumablesController.GetLowStock). Scoped low-stock list (Remaining ≤ MinAmt) — verbatim.
/// </summary>
public record GetConsumableLowStockQuery : IRequest<IReadOnlyList<ConsumableLowStockRowDto>>;

public class GetConsumableLowStockQueryHandler : IRequestHandler<GetConsumableLowStockQuery, IReadOnlyList<ConsumableLowStockRowDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetConsumableLowStockQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<IReadOnlyList<ConsumableLowStockRowDto>> Handle(GetConsumableLowStockQuery request, CancellationToken cancellationToken)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var scoped = await _context.Consumables.Include(c => c.Checkouts).Include(c => c.SparePartMovements).AsNoTracking()
            .Where(c => userCompanyId == null || c.CompanyId == null || c.CompanyId == userCompanyId.Value)
            .ToListAsync(cancellationToken);
        return scoped
            .Select(c => new { c, Remaining = InventoryRemaining.ForConsumable(c) })
            .Where(x => x.Remaining <= x.c.MinAmt)
            .Select(x => new ConsumableLowStockRowDto(x.c.Id, x.c.Name, x.c.ItemNo, x.c.Qty, x.c.MinAmt, x.Remaining))
            .ToList();
    }
}
