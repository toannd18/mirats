using aspire_react.Server.Application.Common.Interfaces;
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
        var items = await _context.Consumables.Include(c => c.Checkouts).AsNoTracking()
            .Where(c => (userCompanyId == null || c.CompanyId == null || c.CompanyId == userCompanyId.Value)
                        && (c.Qty - c.Checkouts.Sum(ch => ch.Quantity)) <= c.MinAmt)
            .Select(c => new ConsumableLowStockRowDto(c.Id, c.Name, c.ItemNo, c.Qty, c.MinAmt, c.Qty - c.Checkouts.Sum(ch => ch.Quantity)))
            .ToListAsync(cancellationToken);
        return items;
    }
}
