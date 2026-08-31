using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Web.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogVisibilityService _actionLogVisibility;
    public DashboardController(AppDbContext context, ICompanyScopeService companyScope, IActionLogVisibilityService actionLogVisibility)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogVisibility = actionLogVisibility;
    }

    [HttpGet("summary")]
    [Authorize]
    public async Task<IActionResult> GetSummary()
    {
        var now = DateTime.UtcNow;
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var assets = _context.Assets.AsNoTracking()
            .Where(a => userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value);
        var consumables = _context.Consumables.Include(c => c.Checkouts).AsNoTracking()
            .Where(c => userCompanyId == null || c.CompanyId == null || c.CompanyId == userCompanyId.Value);
        var accessories = _context.Accessories.Include(a => a.Checkouts).AsNoTracking()
            .Where(a => userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value);
        var components = _context.Components.Include(c => c.Assignments).AsNoTracking()
            .Where(c => userCompanyId == null || c.CompanyId == null || c.CompanyId == userCompanyId.Value);

        var totalAssets = await assets.CountAsync();
        var deployed = await assets.CountAsync(a => a.CurrentAssignmentId != null);
        // Pending assets are available (not deployed, not archived)
        var rtd = await assets.CountAsync(a => a.Status == AssetStatus.Pending && a.CurrentAssignmentId == null);
        var overdueAudits = await assets.CountAsync(a => a.NextAuditDate != null && a.NextAuditDate < now && a.Status != AssetStatus.Archived);
        var archived = await assets.CountAsync(a => a.Status == AssetStatus.Archived);

        var totalValue = await assets.SumAsync(a => a.PurchaseCost ?? 0);

        var lowConsumables = await consumables.CountAsync(c => (c.Qty - c.Checkouts.Sum(ch => (int?)ch.Quantity ?? 0)) <= c.MinAmt);
        var lowAccessories = await accessories.CountAsync(a => (a.Qty - a.Checkouts.Sum(ch => (int?)(ch.AssignedQty - ch.ReturnedQty) ?? 0)) <= a.MinAmt);
        var lowComponents = await components.CountAsync(c => (c.Qty - c.Assignments.Sum(a => (int?)a.AssignedQty ?? 0)) <= c.MinAmt);

        // [MC-4] Systems with an overdue maintenance schedule — same company-scoped count pattern as
        // overdueAudits. A system is "quá hạn" when its next maintenance due date is in the past
        // (NextMaintenanceDueDate is computed at campaign Complete; NULL = never completed → not counted).
        var systemsOverdueMaintenance = await _context.SystemInfos.AsNoTracking()
            .Where(s => userCompanyId == null || s.CompanyId == null || s.CompanyId == userCompanyId.Value)
            .CountAsync(s => s.NextMaintenanceDueDate != null && s.NextMaintenanceDueDate < now);

        return Ok(new
        {
            status = "success",
            data = new
            {
                totalAssets,
                deployedAssets = deployed,
                rtdAssets = rtd,
                overdueAudits,
                archivedAssets = archived,
                lowStockCount = lowConsumables + lowAccessories + lowComponents,
                systemsOverdueMaintenance,
                totalAssetValue = totalValue
            }
        });
    }

    [HttpGet("recent-activity")]
    [Authorize]
    public async Task<IActionResult> GetRecentActivity()
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var candidates = await _context.ActionLogs
            .Include(l => l.Creator)
            .AsNoTracking()
            .Where(l => l.DeletedAt == null)
            .OrderByDescending(l => l.ActionDate)
            .Take(200)
            .ToListAsync();

        var visible = userCompanyId == null
            ? candidates.Take(20).ToList()
            : (await _actionLogVisibility.FilterVisibleLogsAsync(candidates, userCompanyId.Value)).Take(20).ToList();

        var itemIds = visible.Select(l => l.ItemId).Distinct().ToList();
        var assetNames = await _context.Assets.AsNoTracking().Where(x => itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => $"{x.Name} ({x.AssetTag})");
        var consumableNames = await _context.Consumables.AsNoTracking().Where(x => itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name);
        var accessoryNames = await _context.Accessories.AsNoTracking().Where(x => itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name);
        var componentNames = await _context.Components.AsNoTracking().Where(x => itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name);
        var licenseNames = await _context.Licenses.AsNoTracking().Where(x => itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name);
        var userNames = await _context.Users.AsNoTracking().Where(x => itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => $"{x.FirstName} {x.LastName}".Trim());
        var systemNames = await _context.SystemInfos.AsNoTracking().Where(x => itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => $"{x.Name} ({x.Code})");
        var maintenanceNames = await _context.AssetMaintenances.AsNoTracking().Where(x => itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Title);

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
            .Select(l => new
            {
                l.Id,
                l.ItemType,
                l.ItemId,
                l.ActionType,
                l.Note,
                l.LogMeta,
                l.ActionDate,
                ItemName = ResolveItemName(l),
                Creator = new { l.Creator.Id, l.Creator.Username, l.Creator.FirstName, l.Creator.LastName }
            })
            .ToList();

        return Ok(new { status = "success", data = logs });
    }

    [HttpGet("assets-by-status")]
    [Authorize]
    public async Task<IActionResult> GetAssetsByStatus()
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var data = await _context.Assets
            .AsNoTracking()
            .Where(a => (userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value)
                        && a.Status != AssetStatus.Archived)
            .GroupBy(a => a.Status)
            .Select(g => new { status = g.Key.ToString(), count = g.Count() })
            .ToListAsync();

        return Ok(new { status = "success", data });
    }

    [HttpGet("assets-by-category")]
    [Authorize]
    public async Task<IActionResult> GetAssetsByCategory()
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var data = await _context.Assets
            .AsNoTracking()
            .Where(a => (userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value)
                        && a.Status != AssetStatus.Archived && a.Model != null && a.Model.Category != null)
            .GroupBy(a => new { a.Model!.Category!.Name, a.Model.Category.TagColor })
            .Select(g => new { category = g.Key.Name, color = g.Key.TagColor, count = g.Count() })
            .ToListAsync();

        return Ok(new { status = "success", data });
    }

    [HttpGet("low-stock")]
    [Authorize]
    public async Task<IActionResult> GetLowStock()
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var consumables = await _context.Consumables.Include(c => c.Checkouts).AsNoTracking()
            .Where(c => (userCompanyId == null || c.CompanyId == null || c.CompanyId == userCompanyId.Value)
                        && (c.Qty - c.Checkouts.Sum(ch => (int?)ch.Quantity ?? 0)) <= c.MinAmt)
            .Select(c => new { c.Id, c.Name, c.Qty, c.MinAmt, Remaining = c.Qty - c.Checkouts.Sum(ch => (int?)ch.Quantity ?? 0), Type = "Consumable" })
            .Take(10).ToListAsync();

        var accessories = await _context.Accessories.Include(a => a.Checkouts).AsNoTracking()
            .Where(a => (userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value)
                        && (a.Qty - a.Checkouts.Sum(ch => (int?)(ch.AssignedQty - ch.ReturnedQty) ?? 0)) <= a.MinAmt)
            .Select(a => new { a.Id, a.Name, a.Qty, a.MinAmt, Remaining = a.Qty - a.Checkouts.Sum(ch => (int?)(ch.AssignedQty - ch.ReturnedQty) ?? 0), Type = "Accessory" })
            .Take(10).ToListAsync();

        var components = await _context.Components.Include(c => c.Assignments).AsNoTracking()
            .Where(c => (userCompanyId == null || c.CompanyId == null || c.CompanyId == userCompanyId.Value)
                        && (c.Qty - c.Assignments.Sum(a => (int?)a.AssignedQty ?? 0)) <= c.MinAmt)
            .Select(c => new { c.Id, c.Name, c.Qty, c.MinAmt, Remaining = c.Qty - c.Assignments.Sum(a => (int?)a.AssignedQty ?? 0), Type = "Component" })
            .Take(10).ToListAsync();

        var all = consumables.Concat(accessories).Concat(components).ToList();
        return Ok(new { status = "success", data = all });
    }

    [HttpGet("monthly-checkout-trend")]
    [Authorize]
    public async Task<IActionResult> GetMonthlyTrend()
    {
        var twelveMonthsAgo = DateTime.UtcNow.AddMonths(-12);
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var visibleAssetIds = userCompanyId == null
            ? null
            : await _context.Assets.AsNoTracking()
                .Where(a => a.CompanyId == null || a.CompanyId == userCompanyId.Value)
                .Select(a => a.Id).ToListAsync();
        var data = await _context.ActionLogs
            .AsNoTracking()
            .Where(l => l.ItemType == ItemType.Asset && l.ActionDate >= twelveMonthsAgo && l.DeletedAt == null
                        && (visibleAssetIds == null || visibleAssetIds.Contains(l.ItemId)))
            .GroupBy(l => new { l.ActionDate.Year, l.ActionDate.Month })
            .Select(g => new
            {
                month = $"{g.Key.Year}-{g.Key.Month:D2}",
                checkoutCount = g.Count(l => l.ActionType == ActionType.Checkout),
                checkinCount = g.Count(l => l.ActionType == ActionType.Checkin)
            })
            .OrderBy(x => x.month)
            .ToListAsync();

        return Ok(new { status = "success", data });
    }
}