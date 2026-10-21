using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Systems.Queries;

public sealed record SystemSparePartDto(
    Guid Id,
    string Type,
    string Name,
    string? Code,
    int Remaining,
    int MinQty,
    bool IsSufficient,
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
            .Include(r => r.Consumable).ThenInclude(c => c!.Checkouts)
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
                ? (r.Component!.TrackingType == TrackingType.Serial
                    ? r.Component.Units.Count(u => u.Status == ComponentUnitStatus.InStock)
                    : r.Component.Qty - r.Component.Assignments.Sum(a => a.AssignedQty))
                : r.Consumable!.Qty - r.Consumable.Checkouts.Sum(c => c.Quantity);

            return new SystemSparePartDto(
                r.Id,
                isComponent ? "Component" : "Consumable",
                name,
                code,
                remaining,
                r.MinQty,
                remaining >= r.MinQty,
                r.Note);
        }).ToList();

        return result;
    }
}
