using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Common.Inventory;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Systems.Queries;

/// <summary>
/// [FEAT-SPARE 3] One item currently held by a system. Quantities are derived exclusively from the
/// append-only movement ledger: <see cref="IssuedToSystem"/> is the net issued quantity for this
/// system, while <see cref="Remaining"/> is the shared inventory stock.
/// </summary>
public sealed record SystemSparePartDto(
    Guid ItemId,
    string Type,
    string Name,
    string? Code,
    int IssuedToSystem,
    int Remaining,
    bool IsSerial,
    DateTime? LastIssuedAt);

/// <summary>
/// [FEAT-SPARE 3] Items with a positive net issued quantity for one system. There is deliberately
/// no per-system quota anymore — spare parts are shared inventory.
/// </summary>
public record GetSystemSparePartsQuery(Guid SystemId) : IRequest<IReadOnlyList<SystemSparePartDto>?>;

public class GetSystemSparePartsQueryHandler
    : IRequestHandler<GetSystemSparePartsQuery, IReadOnlyList<SystemSparePartDto>?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetSystemSparePartsQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<IReadOnlyList<SystemSparePartDto>?> Handle(
        GetSystemSparePartsQuery request, CancellationToken cancellationToken)
    {
        if (!await SystemsVisibility.IsSystemVisibleAsync(_context, _companyScope, request.SystemId, cancellationToken))
            return null;

        var movements = await _context.SystemSparePartMovements.AsNoTracking()
            .Where(m => m.SystemInfoId == request.SystemId)
            .ToListAsync(cancellationToken);

        var componentIds = movements.Where(m => m.ComponentId.HasValue)
            .Select(m => m.ComponentId!.Value).Distinct().ToList();
        var consumableIds = movements.Where(m => m.ConsumableId.HasValue)
            .Select(m => m.ConsumableId!.Value).Distinct().ToList();

        var components = await _context.Components.AsNoTracking()
            .Where(c => componentIds.Contains(c.Id))
            .Include(c => c.Units)
            .Include(c => c.Assignments)
            .Include(c => c.SparePartMovements)
            .ToDictionaryAsync(c => c.Id, cancellationToken);
        var consumables = await _context.Consumables.AsNoTracking()
            .Where(c => consumableIds.Contains(c.Id))
            .Include(c => c.Checkouts)
            .Include(c => c.SparePartMovements)
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        return movements
            .GroupBy(m => new { m.ComponentId, m.ConsumableId })
            .Select(g =>
            {
                var first = g.First();
                var isComponent = first.ComponentId.HasValue;
                var issued = InventoryRemaining.IssuedToSystem(g, request.SystemId);
                if (issued <= 0) return null;

                Component? component = null;
                Consumable? consumable = null;
                if (isComponent)
                {
                    if (!components.TryGetValue(first.ComponentId!.Value, out component)) return null;
                }
                else if (!consumables.TryGetValue(first.ConsumableId!.Value, out consumable))
                {
                    return null;
                }

                var name = isComponent ? component!.Name : consumable!.Name;
                var code = isComponent
                    ? (component!.ItemNo ?? component.Serial)
                    : consumable!.ItemNo;
                var remaining = isComponent
                    ? InventoryRemaining.ForComponent(component!)
                    : InventoryRemaining.ForConsumable(consumable!);
                var isSerial = isComponent && component!.TrackingType == TrackingType.Serial;
                var lastIssuedAt = g.Where(m => m.Direction == SparePartMovementDirection.Issue)
                    .Select(m => (DateTime?)m.CreatedAt)
                    .Max();

                return new SystemSparePartDto(
                    first.ComponentId ?? first.ConsumableId!.Value,
                    isComponent ? "Component" : "Consumable",
                    name,
                    code,
                    issued,
                    remaining,
                    isSerial,
                    lastIssuedAt);
            })
            .Where(x => x != null)
            .Select(x => x!)
            .OrderBy(x => x.Type)
            .ThenBy(x => x.Name)
            .ToList();
    }
}
