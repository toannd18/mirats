using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Infrastructure.Services;

/// <summary>
/// Company-visibility filter for a bounded list of materialized action-logs (Task S1). Extracted from
/// the identical private methods previously duplicated in <c>ReportsController</c> and
/// <c>DashboardController</c> (both had a byte-for-byte copy). Centralizing it means a company-scoping
/// bug fix applies in exactly one place instead of two that could drift apart.
/// </summary>
public interface IActionLogVisibilityService
{
    /// <summary>
    /// Filters a bounded list of materialized action-logs down to those whose item belongs to the
    /// given user's company (or is company-less / floater). Resolves item companies in batched
    /// queries (one round-trip per item type) to avoid an N+1 per log row.
    /// </summary>
    Task<List<ActionLog>> FilterVisibleLogsAsync(IReadOnlyList<ActionLog> logs, Guid userCompanyId);
}

/// <inheritdoc cref="IActionLogVisibilityService"/>
public class ActionLogVisibilityService : IActionLogVisibilityService
{
    private readonly AppDbContext _context;

    public ActionLogVisibilityService(AppDbContext context) => _context = context;

    public async Task<List<ActionLog>> FilterVisibleLogsAsync(IReadOnlyList<ActionLog> logs, Guid userCompanyId)
    {
        var assets = logs.Where(l => l.ItemType == ItemType.Asset).Select(l => l.ItemId).Distinct().ToList();
        var visibleAssets = new HashSet<Guid>(await _context.Assets.AsNoTracking()
            .Where(a => assets.Contains(a.Id) && (a.CompanyId == null || a.CompanyId == userCompanyId)).Select(a => a.Id).ToListAsync());

        var consumables = logs.Where(l => l.ItemType == ItemType.Consumable).Select(l => l.ItemId).Distinct().ToList();
        var visibleConsumables = new HashSet<Guid>(await _context.Consumables.AsNoTracking()
            .Where(c => consumables.Contains(c.Id) && (c.CompanyId == null || c.CompanyId == userCompanyId)).Select(c => c.Id).ToListAsync());

        var accessories = logs.Where(l => l.ItemType == ItemType.Accessory).Select(l => l.ItemId).Distinct().ToList();
        var visibleAccessories = new HashSet<Guid>(await _context.Accessories.AsNoTracking()
            .Where(a => accessories.Contains(a.Id) && (a.CompanyId == null || a.CompanyId == userCompanyId)).Select(a => a.Id).ToListAsync());

        var components = logs.Where(l => l.ItemType == ItemType.Component).Select(l => l.ItemId).Distinct().ToList();
        var visibleComponents = new HashSet<Guid>(await _context.Components.AsNoTracking()
            .Where(c => components.Contains(c.Id) && (c.CompanyId == null || c.CompanyId == userCompanyId)).Select(c => c.Id).ToListAsync());

        var licenses = logs.Where(l => l.ItemType == ItemType.License).Select(l => l.ItemId).Distinct().ToList();
        var visibleLicenses = new HashSet<Guid>(await _context.Licenses.AsNoTracking()
            .Where(l => licenses.Contains(l.Id) && l.DeletedAt == null && (l.CompanyId == null || l.CompanyId == userCompanyId)).Select(l => l.Id).ToListAsync());

        var unitIds = logs.Where(l => l.ItemType == ItemType.ComponentUnit).Select(l => l.ItemId).Distinct().ToList();
        var visibleUnits = new HashSet<Guid>(await _context.ComponentUnits.AsNoTracking()
            .Where(u => unitIds.Contains(u.Id) && (u.Component.CompanyId == null || u.Component.CompanyId == userCompanyId)).Select(u => u.Id).ToListAsync());

        return logs.Where(l => l.ItemType switch
        {
            ItemType.Asset => visibleAssets.Contains(l.ItemId),
            ItemType.Consumable => visibleConsumables.Contains(l.ItemId),
            ItemType.Accessory => visibleAccessories.Contains(l.ItemId),
            ItemType.Component => visibleComponents.Contains(l.ItemId),
            ItemType.License => visibleLicenses.Contains(l.ItemId),
            ItemType.ComponentUnit => visibleUnits.Contains(l.ItemId),
            _ => false
        }).ToList();
    }
}
