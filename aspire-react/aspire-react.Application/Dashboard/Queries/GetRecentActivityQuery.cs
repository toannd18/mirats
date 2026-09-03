using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Dashboard.Queries;

public record RecentActivityCreatorDto(Guid Id, string Username, string FirstName, string LastName);

public record RecentActivityItemDto(
    Guid Id,
    ItemType ItemType,
    Guid ItemId,
    ActionType ActionType,
    string? Note,
    string? LogMeta,
    DateTime ActionDate,
    string? ItemName,
    RecentActivityCreatorDto Creator);

/// <summary>
/// [Giai đoạn 2-cuối] GET /api/v1/dashboard/recent-activity (extracted verbatim from
/// DashboardController.GetRecentActivity). Superuser: latest 20 logs across all companies;
/// regular user: latest 20 of visible logs (IActionLogVisibilityService — company filter
/// for a bounded 200-row candidate list, verbatim). Item names resolved in batched
/// dictionary lookups across 8 item types (verbatim).
/// </summary>
public record GetRecentActivityQuery : IRequest<IReadOnlyList<RecentActivityItemDto>>;

public class GetRecentActivityQueryHandler : IRequestHandler<GetRecentActivityQuery, IReadOnlyList<RecentActivityItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogVisibilityService _actionLogVisibility;

    public GetRecentActivityQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogVisibilityService actionLogVisibility)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogVisibility = actionLogVisibility;
    }

    public async Task<IReadOnlyList<RecentActivityItemDto>> Handle(GetRecentActivityQuery request, CancellationToken cancellationToken)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var candidates = await _context.ActionLogs
            .Include(l => l.Creator)
            .AsNoTracking()
            .Where(l => l.DeletedAt == null)
            .OrderByDescending(l => l.ActionDate)
            .Take(200)
            .ToListAsync(cancellationToken);

        var visible = userCompanyId == null
            ? candidates.Take(20).ToList()
            : (await _actionLogVisibility.FilterVisibleLogsAsync(candidates, userCompanyId.Value)).Take(20).ToList();

        var itemIds = visible.Select(l => l.ItemId).Distinct().ToList();
        var assetNames = await _context.Assets.AsNoTracking().Where(x => itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => $"{x.Name} ({x.AssetTag})", cancellationToken);
        var consumableNames = await _context.Consumables.AsNoTracking().Where(x => itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var accessoryNames = await _context.Accessories.AsNoTracking().Where(x => itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var componentNames = await _context.Components.AsNoTracking().Where(x => itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var licenseNames = await _context.Licenses.AsNoTracking().Where(x => itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var userNames = await _context.Users.AsNoTracking().Where(x => itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => $"{x.FirstName} {x.LastName}".Trim(), cancellationToken);
        var systemNames = await _context.SystemInfos.AsNoTracking().Where(x => itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => $"{x.Name} ({x.Code})", cancellationToken);
        var maintenanceNames = await _context.AssetMaintenances.AsNoTracking().Where(x => itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Title, cancellationToken);

        string? ResolveItemName(ActionLog l) => l.ItemType switch
        {
            ItemType.Asset => assetNames.GetValueOrDefault(l.ItemId),
            ItemType.Consumable => consumableNames.GetValueOrDefault(l.ItemId),
            ItemType.Accessory => accessoryNames.GetValueOrDefault(l.ItemId),
            ItemType.Component => componentNames.GetValueOrDefault(l.ItemId),
            ItemType.License => licenseNames.GetValueOrDefault(l.ItemId),
            ItemType.User => userNames.GetValueOrDefault(l.ItemId),
            ItemType.SystemInfo => systemNames.GetValueOrDefault(l.ItemId),
            ItemType.AssetMaintenance => maintenanceNames.GetValueOrDefault(l.ItemId),
            _ => null,
        };

        var logs = visible
            .Select(l => new RecentActivityItemDto(
                l.Id,
                l.ItemType,
                l.ItemId,
                l.ActionType,
                l.Note,
                l.LogMeta,
                l.ActionDate,
                ResolveItemName(l),
                l.Creator == null ? null! : new RecentActivityCreatorDto(l.Creator.Id, l.Creator.Username, l.Creator.FirstName, l.Creator.LastName)))
            .ToList();

        return logs;
    }
}
