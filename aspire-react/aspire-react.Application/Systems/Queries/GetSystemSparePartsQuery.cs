using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Common.Inventory;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Systems.Queries;

public sealed record SystemSparePartDto(
    Guid Id,
    Guid ItemId,
    string Type,
    string Name,
    string? Code,
    int Remaining,
    int IssuedToSystem,
    int MinQty,
    bool IsSufficient,
    bool IsSerial,
    string? Note);

/// <summary>
/// [FEAT-SPARE 1] Read-only spare-part requirements for one system. Remaining quantity uses the
/// SAME formulas as the Component/Consumable list endpoints.
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

        var rows = await _context.SystemSparePartRequirements.AsNoTracking()
            .Where(r => r.SystemInfoId == request.SystemId && r.IsActive)
            .Include(r => r.Component).ThenInclude(c => c!.Units)
            .Include(r => r.Component).ThenInclude(c => c!.Assignments)
            .Include(r => r.Component).ThenInclude(c => c!.SparePartMovements)
            .Include(r => r.Consumable).ThenInclude(c => c!.Checkouts)
            .Include(r => r.Consumable).ThenInclude(c => c!.SparePartMovements)
            .OrderBy(r => r.ComponentId != null ? 0 : 1)
            .ThenBy(r => r.Component != null ? r.Component.Name : r.Consumable!.Name)
            .ToListAsync(cancellationToken);

        var result = rows.Select(r =>
        {
            var isComponent = r.Component != null;
            var name = isComponent ? r.Component!.Name : r.Consumable!.Name;
            var code = isComponent
                ? (r.Component!.ItemNo ?? r.Component.Serial)
                : r.Consumable!.ItemNo;
            var remaining = isComponent
                ? InventoryRemaining.ForComponent(r.Component!)
                : InventoryRemaining.ForConsumable(r.Consumable!);
            var issued = isComponent
                ? InventoryRemaining.IssuedToSystem(r.Component!.SparePartMovements, request.SystemId)
                : InventoryRemaining.IssuedToSystem(r.Consumable!.SparePartMovements, request.SystemId);

            return new SystemSparePartDto(
                r.Id,
                r.ComponentId ?? r.ConsumableId!.Value,
                isComponent ? "Component" : "Consumable",
                name,
                code,
                remaining,
                issued,
                r.MinQty,
                remaining >= r.MinQty,
                isComponent && r.Component!.TrackingType == TrackingType.Serial,
                r.Note);
        }).ToList();

        return result;
    }
}
