using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.ActionLogs.Queries;

public record ActionLogEntryDto(
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
    string? LocationName,
    string? TargetSystemInfoName,
    DateTime ActionDate);

/// <summary>
/// [Giai đoạn 3] GET /api/v1/action-logs?itemType&itemId (extracted from
/// ActionLogsController.GetActionLogs). Item audit-history with batch-resolved target names.
/// Company-scoping VERBATIM: IsItemVisibleAsync — superuser (userCompanyId == null) sees
/// everything INCLUDING non-existent items (200 + empty list — pre-migration behavior);
/// a regular user only sees items of their own company (or company-less / floater); item
/// types with no company concept resolve to FALSE (fail closed) → NULL → controller 404
/// "Không tìm thấy lịch sử.".
/// NOTE: single-item visibility is a DIFFERENT operation from
/// IActionLogVisibilityService.FilterVisibleLogsAsync (bounded-list filter used by
/// Dashboard/Reports) — kept as a private handler method ( duyệt phương án (a)).
/// Read-only: no Commands, no markers.
/// </summary>
public record GetActionLogsQuery(ItemType ItemType, Guid ItemId) : IRequest<IReadOnlyList<ActionLogEntryDto>?>;

public class GetActionLogsQueryHandler : IRequestHandler<GetActionLogsQuery, IReadOnlyList<ActionLogEntryDto>?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetActionLogsQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<IReadOnlyList<ActionLogEntryDto>?> Handle(GetActionLogsQuery request, CancellationToken cancellationToken)
    {
        // Company scoping: a regular user may only view action-logs of items in their company.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (!await IsItemVisibleAsync(request.ItemType, request.ItemId, userCompanyId, cancellationToken))
            return null;

        // Step 1: Materialize logs from DB
        var logs = await _context.ActionLogs
            .Include(l => l.Creator)
            .AsNoTracking()
            .Where(l => l.ItemType == request.ItemType && l.ItemId == request.ItemId)
            .OrderByDescending(l => l.ActionDate)
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
                    ? (l.Creator.FirstName + " " + l.Creator.LastName).Trim() != ""
                        ? (l.Creator.FirstName + " " + l.Creator.LastName).Trim()
                        : l.Creator.Username
                    : null,
                l.Note,
                l.LogMeta,
                l.LocationName,
                l.TargetSystemInfoName,
                l.ActionDate
            })
            .ToListAsync(cancellationToken);

        // Step 2: Batch-resolve all target names to avoid N+1
        var targetIds = logs
            .Where(l => l.TargetId.HasValue)
            .Select(l => l.TargetId!.Value)
            .Distinct()
            .ToList();

        // Pre-fetch all entity name mappings in one round trip per table
        var userNames = targetIds.Count > 0
            ? await _context.Users.Where(u => targetIds.Contains(u.Id))
                .Select(u => new { u.Id, Name = (u.FirstName + " " + u.LastName).Trim() != "" ? (u.FirstName + " " + u.LastName).Trim() : u.Username })
                .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        var locationNames = targetIds.Count > 0
            ? await _context.Locations.Where(l => targetIds.Contains(l.Id))
                .Select(l => new { l.Id, l.Name })
                .ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        var departmentNames = targetIds.Count > 0
            ? await _context.Departments.Where(d => targetIds.Contains(d.Id))
                .Select(d => new { d.Id, d.Name })
                .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        var positionNames = targetIds.Count > 0
            ? await _context.SystemPositions.Where(sp => targetIds.Contains(sp.Id))
                .Select(sp => new { sp.Id, sp.Name })
                .ToDictionaryAsync(sp => sp.Id, sp => sp.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        var assetNames = await ResolveAssetNamesAsync(targetIds, cancellationToken);

        // Step 3: Enrich with resolved target names
        var enriched = logs.Select(log =>
        {
            string? targetName = null;

            if (log.TargetId.HasValue && log.TargetType != null)
            {
                var tt = Enum.Parse<AssignmentTargetType>(log.TargetType);
                targetName = tt switch
                {
                    AssignmentTargetType.User => userNames.GetValueOrDefault(log.TargetId.Value),
                    AssignmentTargetType.SystemPosition => positionNames.GetValueOrDefault(log.TargetId.Value)
                        ?? locationNames.GetValueOrDefault(log.TargetId.Value),
                    AssignmentTargetType.Asset => assetNames.GetValueOrDefault(log.TargetId.Value),
                    _ => null
                };
            }

            // Fallback: if TargetType is null/unknown or entity wasn't found in the typed lookup,
            // try searching all tables by ID.
            if (targetName == null && log.TargetId.HasValue)
            {
                var tid = log.TargetId.Value;
                targetName = userNames.GetValueOrDefault(tid)
                    ?? locationNames.GetValueOrDefault(tid)
                    ?? departmentNames.GetValueOrDefault(tid)
                    ?? positionNames.GetValueOrDefault(tid)
                    ?? assetNames.GetValueOrDefault(tid);
            }

            return new ActionLogEntryDto(
                log.Id,
                log.ItemType,
                log.ItemId,
                log.ActionType,
                log.ActionTypeValue,
                log.TargetType,
                log.TargetId,
                targetName,
                log.CreatorName,
                log.Note,
                log.LogMeta,
                log.LocationName,
                log.TargetSystemInfoName,
                log.ActionDate);
        }).ToList();

        return enriched;
    }

    /// <summary>Resolves Asset display names (AssetTag - Name) — shared by /action-logs and /by-system.</summary>
    private async Task<Dictionary<Guid, string>> ResolveAssetNamesAsync(List<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0) return new Dictionary<Guid, string>();
        return await _context.Assets.Where(a => ids.Contains(a.Id))
            .Select(a => new { a.Id, Name = a.AssetTag + " - " + a.Name })
            .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);
    }

    /// <summary>
    /// Returns whether the current user (given their company scope) may view the action-logs of an
    /// item. Superuser (userCompanyId == null) sees everything; a regular user may only see items of
    /// their own company (or company-less / floater). Types with no company concept resolve to false
    /// (fail closed) for regular users.
    /// </summary>
    private async Task<bool> IsItemVisibleAsync(ItemType itemType, Guid itemId, Guid? userCompanyId, CancellationToken cancellationToken)
    {
        if (!userCompanyId.HasValue) return true;

        return itemType switch
        {
            ItemType.Asset => await _context.Assets.AsNoTracking().AnyAsync(a => a.Id == itemId && (a.CompanyId == null || a.CompanyId == userCompanyId.Value), cancellationToken),
            ItemType.Consumable => await _context.Consumables.AsNoTracking().AnyAsync(c => c.Id == itemId && (c.CompanyId == null || c.CompanyId == userCompanyId.Value), cancellationToken),
            ItemType.Accessory => await _context.Accessories.AsNoTracking().AnyAsync(a => a.Id == itemId && (a.CompanyId == null || a.CompanyId == userCompanyId.Value), cancellationToken),
            ItemType.Component => await _context.Components.AsNoTracking().AnyAsync(c => c.Id == itemId && (c.CompanyId == null || c.CompanyId == userCompanyId.Value), cancellationToken),
            ItemType.License => await _context.Licenses.AsNoTracking().AnyAsync(l => l.Id == itemId && l.DeletedAt == null && (l.CompanyId == null || l.CompanyId == userCompanyId.Value), cancellationToken),
            ItemType.ComponentUnit => await _context.ComponentUnits.AsNoTracking().AnyAsync(u => u.Id == itemId && (u.Component.CompanyId == null || u.Component.CompanyId == userCompanyId.Value), cancellationToken),
            _ => false
        };
    }
}
