using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.ComponentUnits.Queries;

public record ComponentUnitLogEntryDto(
    Guid Id,
    string ItemType,
    Guid ItemId,
    string ActionType,
    int ActionTypeValue,
    string? TargetType,
    Guid? TargetId,
    string? TargetName,
    string? CreatorName,
    string? Note,
    string? LogMeta,
    DateTime ActionDate,
    string? LocationName,
    string? TargetSystemInfoName);

public record ComponentUnitLogsResult(IReadOnlyList<ComponentUnitLogEntryDto> Items, int Total);

/// <summary>
/// [Giai đoạn 3] GET /api/v1/component-units/{unitId}/action-logs (extracted from
/// ComponentUnitsController.GetActionLogs). Single-unit audit history with Asset-name resolution
/// ("TAG - Name"). Visibility VERBATIM: a serial unit's history is only visible to users of its
/// component's company (superuser all) → NULL → controller 404 "ComponentUnit not found.".
/// Read-only: no Commands, no markers.
/// </summary>
public record GetComponentUnitLogsQuery(Guid UnitId, int Page = 1, int PageSize = 20)
    : IRequest<ComponentUnitLogsResult?>;

public class GetComponentUnitLogsQueryHandler : IRequestHandler<GetComponentUnitLogsQuery, ComponentUnitLogsResult?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetComponentUnitLogsQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<ComponentUnitLogsResult?> Handle(GetComponentUnitLogsQuery request, CancellationToken cancellationToken)
    {
        // Company scoping: a serial unit's history is only visible to users of its component's company.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var visible = await _context.ComponentUnits.AsNoTracking()
            .AnyAsync(u => u.Id == request.UnitId && (userCompanyId == null || u.Component.CompanyId == null || u.Component.CompanyId == userCompanyId.Value), cancellationToken);
        if (!visible) return null;

        var query = _context.ActionLogs.Include(l => l.Creator).AsNoTracking()
            .Where(l => l.ItemType == Domain.Enums.ItemType.ComponentUnit && l.ItemId == request.UnitId)
            .OrderByDescending(l => l.ActionDate);

        var total = await query.CountAsync(cancellationToken);
        var logs = await query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(l => new
            {
                l.Id,
                ItemType = l.ItemType.ToString(),
                l.ItemId,
                ActionType = l.ActionType.ToString(),
                ActionTypeValue = (int)l.ActionType,
                TargetType = l.TargetType.HasValue ? l.TargetType.Value.ToString() : null,
                l.TargetId,
                CreatorName = l.Creator != null
                    ? (l.Creator.FirstName + " " + l.Creator.LastName).Trim() != "" ? (l.Creator.FirstName + " " + l.Creator.LastName).Trim() : l.Creator.Username
                    : null,
                l.Note,
                l.LogMeta,
                l.ActionDate,
                l.LocationName,
                l.TargetSystemInfoName
            }).ToListAsync(cancellationToken);

        var targetIds = logs.Where(x => x.TargetId.HasValue).Select(x => x.TargetId!.Value).Distinct().ToList();
        var assetNames = targetIds.Count > 0
            ? await _context.Assets.Where(a => targetIds.Contains(a.Id))
                .Select(a => new { a.Id, Name = a.AssetTag + " - " + a.Name })
                .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        var enriched = logs.Select(log => new ComponentUnitLogEntryDto(
            log.Id,
            log.ItemType,
            log.ItemId,
            log.ActionType,
            log.ActionTypeValue,
            log.TargetType,
            log.TargetId,
            log.TargetId.HasValue ? assetNames.GetValueOrDefault(log.TargetId.Value) : null,
            log.CreatorName,
            log.Note,
            log.LogMeta,
            log.ActionDate,
            log.LocationName,
            log.TargetSystemInfoName)).ToList();

        return new ComponentUnitLogsResult(enriched, total);
    }
}
