using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Components.Queries;

public record ComponentUnitRowDto(Guid Id, string SerialNo, string Status, Guid? CurrentAssetId, string? Notes,
    DateTime CreatedAt, DateTime UpdatedAt, ComponentAssetRefDto? CurrentAsset, bool CanDelete);

public record ComponentUnitsResult(IReadOnlyList<ComponentUnitRowDto> Items, int Total);

/// <summary>
/// [Giai đoạn 3 — Nặng] GET /api/v1/components/{id}/units (extracted from
/// ComponentsController.GetUnits). Scope (parent component visible) → NULL → 404; status filter;
/// canDelete = unit never checked out (log scan); no page clamps (verbatim).
/// </summary>
public record GetComponentUnitsQuery(Guid Id, ComponentUnitStatus? Status = null, int Page = 1, int PageSize = 20)
    : IRequest<ComponentUnitsResult?>;

public class GetComponentUnitsQueryHandler : IRequestHandler<GetComponentUnitsQuery, ComponentUnitsResult?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetComponentUnitsQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<ComponentUnitsResult?> Handle(GetComponentUnitsQuery request, CancellationToken cancellationToken)
    {
        // Company scoping: verify the parent component is visible to the current user.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var visible = await _context.Components.AsNoTracking()
            .AnyAsync(c => c.Id == request.Id && (userCompanyId == null || c.CompanyId == null || c.CompanyId == userCompanyId.Value), cancellationToken);
        if (!visible) return null;

        var query = _context.ComponentUnits
            .Include(u => u.CurrentAsset)
            .Where(u => u.ComponentId == request.Id);
        if (request.Status.HasValue) query = query.Where(u => u.Status == request.Status.Value);

        var total = await query.CountAsync(cancellationToken);
        var units = await query.OrderBy(u => u.CreatedAt).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(u => new
            {
                u.Id,
                u.SerialNo,
                Status = u.Status.ToString(),
                u.CurrentAssetId,
                u.Notes,
                u.CreatedAt,
                u.UpdatedAt,
                CurrentAsset = u.CurrentAsset == null ? null : new { u.CurrentAsset.Id, u.CurrentAsset.AssetTag, u.CurrentAsset.Name }
            }).ToListAsync(cancellationToken);

        // canDelete = the unit has NEVER been checked out (audit history must stay intact).
        var pageUnitIds = units.Select(u => u.Id).ToList();
        var blockedUnits = new HashSet<Guid>();
        if (pageUnitIds.Count > 0)
        {
            blockedUnits = (await _context.ActionLogs.AsNoTracking()
                .Where(l => l.ItemType == ItemType.ComponentUnit && l.ActionType == ActionType.Checkout && pageUnitIds.Contains(l.ItemId))
                .Select(l => l.ItemId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        }
        var result = units.Select(u => new ComponentUnitRowDto(
            u.Id, u.SerialNo, u.Status, u.CurrentAssetId, u.Notes, u.CreatedAt, u.UpdatedAt,
            u.CurrentAsset == null ? null : new ComponentAssetRefDto(u.CurrentAsset.Id, u.CurrentAsset.AssetTag, u.CurrentAsset.Name),
            !blockedUnits.Contains(u.Id))).ToList();
        return new ComponentUnitsResult(result, total);
    }
}
