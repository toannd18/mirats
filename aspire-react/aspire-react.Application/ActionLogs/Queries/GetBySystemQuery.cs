using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.ActionLogs.Queries;

public record ActionLogBySystemEntryDto(
    Guid Id,
    string ItemType,
    Guid ItemId,
    string ActionType,
    int ActionTypeValue,
    string? TargetType,
    Guid? TargetId,
    string? TargetName,
    Guid? TargetSystemInfoId,
    string? CreatorName,
    string? Note,
    string? LogMeta,
    string? LocationName,
    string? TargetSystemInfoName,
    DateTime ActionDate,
    string? ItemName);

public record ActionLogsBySystemResult(IReadOnlyList<ActionLogBySystemEntryDto> Items, int Total);

/// <summary>
/// [Giai đoạn 3] GET /api/v1/action-logs/by-system (extracted from
/// ActionLogsController.GetBySystem). System history — every Asset action that targeted a
/// SystemPosition belonging to one system + MAINTENANCE CAMPAIGN events (MC-3, OR filter —
/// both index-friendly on TargetSystemInfoId). Full-replace paging clamps verbatim
/// (page &lt; 1 → 1; pageSize out of 1..100 → 20).
/// Company isolation VERBATIM (SEC-FIX CS-7): system visibility via GetCurrentUserCompanyIdAsync
/// (superuser unrestricted; regular = company-less systems or own company) → NULL → controller
/// 404 "Hệ thống không tồn tại hoặc ngoài phạm vi công ty của bạn.".
/// Item names resolved per ItemType — 6 branches (Asset "TAG - Name" / Accessory / Consumable /
/// Component / License / MaintenanceCampaign "Bảo dưỡng {sys} ({batch})") — verbatim.
/// Read-only: no Commands, no markers.
/// </summary>
public record GetBySystemQuery(
    Guid SystemInfoId,
    Guid? SystemPositionId = null,
    ActionType? ActionType = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 20) : IRequest<ActionLogsBySystemResult?>;

public class GetBySystemQueryHandler : IRequestHandler<GetBySystemQuery, ActionLogsBySystemResult?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetBySystemQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<ActionLogsBySystemResult?> Handle(GetBySystemQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 100 ? 20 : request.PageSize;

        // ──── Company isolation ────
        // [SEC-FIX CS-7, 2026-08-23] The old gate called GetUserCompanyIdsAsync() — a placeholder
        // that ALWAYS returns [] (CompanyScopeService.cs) → "userCompanyIds.Count == 0" was always
        // true for regular users, so the system-visibility check was a NO-OP and any user could
        // read another company's full system history (verified empirically: cross-company GET
        // returned 200 with logs + asset names). Now uses GetCurrentUserCompanyIdAsync() — the
        // same working pattern as IsItemVisibleAsync. Superuser (null) is unrestricted;
        // a regular user may only view history of company-less systems or their own company's.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var systemVisible = await _context.SystemInfos.AsNoTracking().AnyAsync(s =>
            s.Id == request.SystemInfoId &&
            (userCompanyId == null || s.CompanyId == null || s.CompanyId == userCompanyId.Value), cancellationToken);
        if (!systemVisible)
            return null;

        // ──── Core filter (hot path → indexed on TargetSystemInfoId) ────
        // [MC-3, hướng b] System history now also surfaces MAINTENANCE CAMPAIGN events (create/
        // complete) alongside the existing SystemPosition-targeted asset actions — both carry
        // TargetSystemInfoId = systemInfoId, so the OR stays fully index-friendly.
        var query = _context.ActionLogs
            .AsNoTracking()
            .Where(l => l.TargetSystemInfoId == request.SystemInfoId &&
                        (l.TargetType == AssignmentTargetType.SystemPosition ||
                         l.ItemType == ItemType.MaintenanceCampaign));

        if (request.SystemPositionId.HasValue)
            query = query.Where(l => l.TargetId == request.SystemPositionId.Value);
        if (request.ActionType.HasValue)
            query = query.Where(l => l.ActionType == request.ActionType.Value);
        if (request.From.HasValue)
            query = query.Where(l => l.CreatedAt >= request.From.Value.ToUniversalTime());
        if (request.To.HasValue)
            query = query.Where(l => l.CreatedAt <= request.To.Value.ToUniversalTime());

        var total = await query.CountAsync(cancellationToken);

        // Step 1: Materialize the requested page from DB
        var logs = await query
            .OrderByDescending(l => l.ActionDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new
            {
                l.Id,
                ItemType = l.ItemType.ToString(),
                l.ItemId,
                ActionType = l.ActionType.ToString(),
                ActionTypeValue = (int)l.ActionType,
                TargetType = l.TargetType.HasValue ? l.TargetType.Value.ToString() : null,
                l.TargetId,
                l.TargetSystemInfoId,
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

        // Step 2: Batch-resolve target (SystemPosition) names + item display names per ItemType —
        // same mechanism as DashboardController.GetRecentActivity and GET /action-logs, avoiding
        // N+1 round trips. Accessory/Consumable/Component/License logs are resolved from their own
        // tables (not just Assets) so the "Tài sản" column shows the real item name.
        var targetIds = logs.Where(l => l.TargetId.HasValue).Select(l => l.TargetId!.Value).Distinct().ToList();
        var itemIds = logs.Select(l => l.ItemId).Distinct().ToList();

        var positionNames = await ResolvePositionNamesAsync(targetIds, cancellationToken);
        var assetNames = await _context.Assets.AsNoTracking()
            .Where(a => itemIds.Contains(a.Id))
            .Select(a => new { a.Id, Name = a.AssetTag + " - " + a.Name })
            .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);
        var accessoryNames = await _context.Accessories.AsNoTracking()
            .Where(a => itemIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Name })
            .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);
        var consumableNames = await _context.Consumables.AsNoTracking()
            .Where(c => itemIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var componentNames = await _context.Components.AsNoTracking()
            .Where(c => itemIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var licenseNames = await _context.Licenses.AsNoTracking()
            .Where(l => itemIds.Contains(l.Id))
            .Select(l => new { l.Id, l.Name })
            .ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken);
        // [MC-3] Campaign logs render their display name via the pinned system + batch number.
        var campaignNames = await _context.MaintenanceCampaigns.AsNoTracking()
            .Where(c => itemIds.Contains(c.Id))
            .Select(c => new { c.Id, Name = "Bảo dưỡng " + c.SystemInfo.Name + (c.BatchNumber != null ? " (" + c.BatchNumber + ")" : "") })
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        string? ResolveItemName(string itemType, Guid itemId) => Enum.TryParse<ItemType>(itemType, out var it) ? it switch
        {
            ItemType.Asset => assetNames.GetValueOrDefault(itemId),
            ItemType.Accessory => accessoryNames.GetValueOrDefault(itemId),
            ItemType.Consumable => consumableNames.GetValueOrDefault(itemId),
            ItemType.Component => componentNames.GetValueOrDefault(itemId),
            ItemType.License => licenseNames.GetValueOrDefault(itemId),
            ItemType.MaintenanceCampaign => campaignNames.GetValueOrDefault(itemId),
            _ => null
        } : null;

        var enriched = logs.Select(log => new ActionLogBySystemEntryDto(
            log.Id,
            log.ItemType,
            log.ItemId,
            log.ActionType,
            log.ActionTypeValue,
            log.TargetType,
            log.TargetId,
            positionNames.GetValueOrDefault(log.TargetId ?? Guid.Empty),
            log.TargetSystemInfoId,
            log.CreatorName,
            log.Note,
            log.LogMeta,
            log.LocationName,
            log.TargetSystemInfoName,
            log.ActionDate,
            ResolveItemName(log.ItemType, log.ItemId))).ToList();

        return new ActionLogsBySystemResult(enriched, total);
    }

    /// <summary>Resolves SystemPosition names — used by /by-system to render the "Vị trí lắp đặt" column.</summary>
    private async Task<Dictionary<Guid, string>> ResolvePositionNamesAsync(List<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0) return new Dictionary<Guid, string>();
        return await _context.SystemPositions.Where(sp => ids.Contains(sp.Id))
            .Select(sp => new { sp.Id, sp.Name })
            .ToDictionaryAsync(sp => sp.Id, sp => sp.Name, cancellationToken);
    }
}
