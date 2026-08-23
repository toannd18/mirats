using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Web.Controllers;

[ApiController]
[Route("api/v1/action-logs")]
[Authorize]
public class ActionLogsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public ActionLogsController(AppDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    [HttpGet]
    public async Task<IActionResult> GetActionLogs(
        [FromQuery] ItemType itemType,
        [FromQuery] Guid itemId)
    {
        // Company scoping: a regular user may only view action-logs of items in their company.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (!await IsItemVisibleAsync(itemType, itemId, userCompanyId))
            return NotFound(new { status = "error", message = "Không tìm thấy lịch sử." });

        // Step 1: Materialize logs from DB
        var logs = await _context.ActionLogs
            .Include(l => l.Creator)
            .AsNoTracking()
            .Where(l => l.ItemType == itemType && l.ItemId == itemId)
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
                TargetName = (string?)null, // resolved in Step 2
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
            .ToListAsync();

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
                .ToDictionaryAsync(u => u.Id, u => u.Name)
            : new Dictionary<Guid, string>();

        var locationNames = targetIds.Count > 0
            ? await _context.Locations.Where(l => targetIds.Contains(l.Id))
                .Select(l => new { l.Id, l.Name })
                .ToDictionaryAsync(l => l.Id, l => l.Name)
            : new Dictionary<Guid, string>();

        var departmentNames = targetIds.Count > 0
            ? await _context.Departments.Where(d => targetIds.Contains(d.Id))
                .Select(d => new { d.Id, d.Name })
                .ToDictionaryAsync(d => d.Id, d => d.Name)
            : new Dictionary<Guid, string>();

        var positionNames = targetIds.Count > 0
            ? await _context.SystemPositions.Where(sp => targetIds.Contains(sp.Id))
                .Select(sp => new { sp.Id, sp.Name })
                .ToDictionaryAsync(sp => sp.Id, sp => sp.Name)
            : new Dictionary<Guid, string>();

        var assetNames = await ResolveAssetNamesAsync(targetIds);

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

            return new
            {
                log.Id,
                log.ItemType,
                log.ItemId,
                log.ActionType,
                log.ActionTypeValue,
                log.TargetType,
                log.TargetId,
                TargetName = targetName,
                log.CreatorName,
                log.Note,
                log.LogMeta,
                log.LocationName,
                log.TargetSystemInfoName,
                log.ActionDate
            };
        }).ToList();

        return Ok(new { status = "success", data = enriched });
    }

    /// <summary>
    /// System history — every Asset action that targeted a SystemPosition belonging to one system.
    /// Reuses the same response shape as GET /action-logs, plus the resolved Item (Asset) display
    /// name so the reader knows which asset moved.
    /// </summary>
    [HttpGet("by-system")]
    [Authorize(Policy = "assets.view")]
    public async Task<IActionResult> GetBySystem(
        [FromQuery] Guid systemInfoId,
        [FromQuery] Guid? systemPositionId = null,
        [FromQuery] ActionType? actionType = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 100) pageSize = 20;

        // ──── Company isolation ────
        // [SEC-FIX CS-7, 2026-08-23] The old gate called GetUserCompanyIdsAsync() — a placeholder
        // that ALWAYS returns [] (CompanyScopeService.cs) → "userCompanyIds.Count == 0" was always
        // true for regular users, so the system-visibility check was a NO-OP and any user could
        // read another company's full system history (verified empirically: cross-company GET
        // returned 200 with logs + asset names). Now uses GetCurrentUserCompanyIdAsync() — the
        // same working pattern as IsItemVisibleAsync below. Superuser (null) is unrestricted;
        // a regular user may only view history of company-less systems or their own company's.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var systemVisible = await _context.SystemInfos.AsNoTracking().AnyAsync(s =>
            s.Id == systemInfoId &&
            (userCompanyId == null || s.CompanyId == null || s.CompanyId == userCompanyId.Value));
        if (!systemVisible)
            return NotFound(new { status = "error", message = "Hệ thống không tồn tại hoặc ngoài phạm vi công ty của bạn." });

        // ──── Core filter (hot path → indexed on TargetSystemInfoId) ────
        var query = _context.ActionLogs
            .AsNoTracking()
            .Where(l => l.TargetType == AssignmentTargetType.SystemPosition && l.TargetSystemInfoId == systemInfoId);

        if (systemPositionId.HasValue)
            query = query.Where(l => l.TargetId == systemPositionId.Value);
        if (actionType.HasValue)
            query = query.Where(l => l.ActionType == actionType.Value);
        if (from.HasValue)
            query = query.Where(l => l.CreatedAt >= from.Value.ToUniversalTime());
        if (to.HasValue)
            query = query.Where(l => l.CreatedAt <= to.Value.ToUniversalTime());

        var total = await query.CountAsync();

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
                TargetName = (string?)null, // resolved in Step 2
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
                l.ActionDate,
                ItemName = (string?)null // resolved in Step 2
            })
            .ToListAsync();

        // Step 2: Batch-resolve target (SystemPosition) names + item display names per ItemType —
        // same mechanism as DashboardController.GetRecentActivity and GET /action-logs, avoiding
        // N+1 round trips. Accessory/Consumable/Component/License logs are resolved from their own
        // tables (not just Assets) so the "Tài sản" column shows the real item name.
        var targetIds = logs.Where(l => l.TargetId.HasValue).Select(l => l.TargetId!.Value).Distinct().ToList();
        var itemIds = logs.Select(l => l.ItemId).Distinct().ToList();

        var positionNames = await ResolvePositionNamesAsync(targetIds);
        var assetNames = await _context.Assets.AsNoTracking()
            .Where(a => itemIds.Contains(a.Id))
            .Select(a => new { a.Id, Name = a.AssetTag + " - " + a.Name })
            .ToDictionaryAsync(a => a.Id, a => a.Name);
        var accessoryNames = await _context.Accessories.AsNoTracking()
            .Where(a => itemIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Name })
            .ToDictionaryAsync(a => a.Id, a => a.Name);
        var consumableNames = await _context.Consumables.AsNoTracking()
            .Where(c => itemIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name);
        var componentNames = await _context.Components.AsNoTracking()
            .Where(c => itemIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name);
        var licenseNames = await _context.Licenses.AsNoTracking()
            .Where(l => itemIds.Contains(l.Id))
            .Select(l => new { l.Id, l.Name })
            .ToDictionaryAsync(l => l.Id, l => l.Name);

        string? ResolveItemName(string itemType, Guid itemId) => Enum.TryParse<ItemType>(itemType, out var it) ? it switch
        {
            ItemType.Asset => assetNames.GetValueOrDefault(itemId),
            ItemType.Accessory => accessoryNames.GetValueOrDefault(itemId),
            ItemType.Consumable => consumableNames.GetValueOrDefault(itemId),
            ItemType.Component => componentNames.GetValueOrDefault(itemId),
            ItemType.License => licenseNames.GetValueOrDefault(itemId),
            _ => null
        } : null;

        var enriched = logs.Select(log => new
        {
            log.Id,
            log.ItemType,
            log.ItemId,
            log.ActionType,
            log.ActionTypeValue,
            log.TargetType,
            log.TargetId,
            TargetName = positionNames.GetValueOrDefault(log.TargetId ?? Guid.Empty),
            log.TargetSystemInfoId,
            log.CreatorName,
            log.Note,
            log.LogMeta,
            log.LocationName,
            log.TargetSystemInfoName,
            log.ActionDate,
            ItemName = ResolveItemName(log.ItemType, log.ItemId)
        }).ToList();

        return Ok(new { status = "success", data = enriched, total });
    }

    /// <summary>Resolves Asset display names (AssetTag - Name) — shared by /action-logs and /by-system.</summary>
    private Task<Dictionary<Guid, string>> ResolveAssetNamesAsync(List<Guid> ids)
    {
        if (ids.Count == 0) return Task.FromResult(new Dictionary<Guid, string>());
        return _context.Assets.Where(a => ids.Contains(a.Id))
            .Select(a => new { a.Id, Name = a.AssetTag + " - " + a.Name })
            .ToDictionaryAsync(a => a.Id, a => a.Name);
    }

    /// <summary>Resolves SystemPosition names — used by /by-system to render the "Vị trí lắp đặt" column.</summary>
    private Task<Dictionary<Guid, string>> ResolvePositionNamesAsync(List<Guid> ids)
    {
        if (ids.Count == 0) return Task.FromResult(new Dictionary<Guid, string>());
        return _context.SystemPositions.Where(sp => ids.Contains(sp.Id))
            .Select(sp => new { sp.Id, sp.Name })
            .ToDictionaryAsync(sp => sp.Id, sp => sp.Name);
    }

    /// <summary>
    /// Returns whether the current user (given their company scope) may view the action-logs of an
    /// item. Superuser (userCompanyId == null) sees everything; a regular user may only see items of
    /// their own company (or company-less / floater). Types with no company concept resolve to false
    /// (fail closed) for regular users.
    /// </summary>
    private Task<bool> IsItemVisibleAsync(ItemType itemType, Guid itemId, Guid? userCompanyId)
    {
        if (!userCompanyId.HasValue) return Task.FromResult(true);

        return itemType switch
        {
            ItemType.Asset => _context.Assets.AsNoTracking().AnyAsync(a => a.Id == itemId && (a.CompanyId == null || a.CompanyId == userCompanyId.Value)),
            ItemType.Consumable => _context.Consumables.AsNoTracking().AnyAsync(c => c.Id == itemId && (c.CompanyId == null || c.CompanyId == userCompanyId.Value)),
            ItemType.Accessory => _context.Accessories.AsNoTracking().AnyAsync(a => a.Id == itemId && (a.CompanyId == null || a.CompanyId == userCompanyId.Value)),
            ItemType.Component => _context.Components.AsNoTracking().AnyAsync(c => c.Id == itemId && (c.CompanyId == null || c.CompanyId == userCompanyId.Value)),
            ItemType.License => _context.Licenses.AsNoTracking().AnyAsync(l => l.Id == itemId && l.DeletedAt == null && (l.CompanyId == null || l.CompanyId == userCompanyId.Value)),
            ItemType.ComponentUnit => _context.ComponentUnits.AsNoTracking().AnyAsync(u => u.Id == itemId && (u.Component.CompanyId == null || u.Component.CompanyId == userCompanyId.Value)),
            _ => Task.FromResult(false)
        };
    }
}