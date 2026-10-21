using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Dashboard.Queries;

/// <summary>One row shown when a dashboard warning card is opened.</summary>
public sealed record DashboardAlertItemDto(
    Guid Id,
    string Name,
    string? Code,
    DateTime? DueAt,
    string? Detail);

/// <summary>Count + drill-down items for one warning category.</summary>
public sealed record DashboardAlertDto(
    string Key,
    int Count,
    IReadOnlyList<DashboardAlertItemDto> Items);

public sealed record DashboardNumbersDto(
    int TotalAssets,
    int DeployedAssets,
    int PendingAssets,
    decimal TotalAssetValue);

public sealed record DashboardCampaignDto(
    Guid Id,
    string Name,
    string SystemName,
    int Completed,
    int Total,
    DateTime? EndDate);

/// <summary>
/// [DASHBOARD redesign] Single company-scoped aggregate used by the dashboard page and its
/// warning drill-down lists. Reuses the existing summary/recent/status/category queries so the
/// company-scope and InventoryRemaining rules stay in one place.
/// </summary>
public record GetDashboardOverviewQuery : IRequest<DashboardOverviewDto>;

public sealed record DashboardOverviewDto(
    IReadOnlyList<DashboardAlertDto> Alerts,
    DashboardNumbersDto Numbers,
    IReadOnlyList<DashboardCampaignDto> ActiveCampaigns,
    IReadOnlyList<AssetsByStatusItemDto> ByStatus,
    IReadOnlyList<AssetsByCategoryItemDto> ByCategory,
    IReadOnlyList<RecentActivityItemDto> RecentActivity,
    DateTime UpdatedAt);

public class GetDashboardOverviewQueryHandler
    : IRequestHandler<GetDashboardOverviewQuery, DashboardOverviewDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IMediator _mediator;

    public GetDashboardOverviewQueryHandler(
        IApplicationDbContext context, ICompanyScopeService companyScope, IMediator mediator)
    {
        _context = context;
        _companyScope = companyScope;
        _mediator = mediator;
    }

    public async Task<DashboardOverviewDto> Handle(
        GetDashboardOverviewQuery request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var expiryThreshold = now.AddDays(30);
        var companyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var scope = companyId;

        var summary = await _mediator.Send(new GetDashboardSummaryQuery(), cancellationToken);
        var lowStock = await _mediator.Send(new GetLowStockQuery(null), cancellationToken);
        var recent = await _mediator.Send(new GetRecentActivityQuery(), cancellationToken);

        // [DASHBOARD-DATA] One asset read feeds both status and category tables. Category grouping
        // includes Archived assets and assets without a category ("Chưa phân loại"), so the sum of
        // category rows always equals TotalAssets as well.
        var assetRows = await _context.Assets.AsNoTracking()
            .Where(a => scope == null || a.CompanyId == null || a.CompanyId == scope.Value)
            .Select(a => new
            {
                a.Status,
                Category = a.Model != null && a.Model.Category != null ? a.Model.Category.Name : null,
                Color = a.Model != null && a.Model.Category != null ? a.Model.Category.TagColor : null,
            })
            .ToListAsync(cancellationToken);

        var statusCounts = assetRows
            .GroupBy(a => a.Status)
            .ToDictionary(g => g.Key, g => g.Count());
        var byStatus = Enum.GetValues<AssetStatus>()
            .Select(s => new AssetsByStatusItemDto(s.ToString(), statusCounts.GetValueOrDefault(s)))
            .ToList();
        var totalAssets = byStatus.Sum(x => x.Count);
        var deployedAssets = statusCounts.GetValueOrDefault(AssetStatus.Deployed);
        var pendingAssets = statusCounts.GetValueOrDefault(AssetStatus.Pending);

        var byCategory = assetRows
            .GroupBy(a => new { Category = a.Category ?? "Chưa phân loại", a.Color })
            .Select(g => new AssetsByCategoryItemDto(g.Key.Category, g.Key.Color, g.Count()))
            .OrderBy(x => x.Category == "Chưa phân loại" ? 1 : 0)
            .ThenBy(x => x.Category)
            .ToList();

        var maintenanceItems = await _context.SystemInfos.AsNoTracking()
            .Where(s => (scope == null || s.CompanyId == null || s.CompanyId == scope.Value)
                        && s.NextMaintenanceDueDate != null && s.NextMaintenanceDueDate < now)
            .OrderBy(s => s.NextMaintenanceDueDate)
            .Take(100)
            .Select(s => new DashboardAlertItemDto(
                s.Id, s.Name, s.Code, s.NextMaintenanceDueDate, null))
            .ToListAsync(cancellationToken);

        var auditItems = await _context.Assets.AsNoTracking()
            .Where(a => (scope == null || a.CompanyId == null || a.CompanyId == scope.Value)
                        && a.NextAuditDate != null && a.NextAuditDate < now
                        && a.Status != AssetStatus.Archived)
            .OrderBy(a => a.NextAuditDate)
            .Take(100)
            .Select(a => new DashboardAlertItemDto(
                a.Id, a.Name, a.AssetTag, a.NextAuditDate, null))
            .ToListAsync(cancellationToken);

        var licenseItems = await _context.Licenses.AsNoTracking()
            .Where(l => (scope == null || l.CompanyId == null || l.CompanyId == scope.Value)
                        && l.DeletedAt == null && l.ExpirationDate != null
                        && l.ExpirationDate <= expiryThreshold)
            .OrderBy(l => l.ExpirationDate)
            .Take(100)
            .Select(l => new DashboardAlertItemDto(
                l.Id, l.Name, l.Serial, l.ExpirationDate, null))
            .ToListAsync(cancellationToken);
        var licenseCount = await _context.Licenses.AsNoTracking()
            .CountAsync(l => (scope == null || l.CompanyId == null || l.CompanyId == scope.Value)
                             && l.DeletedAt == null && l.ExpirationDate != null
                             && l.ExpirationDate <= expiryThreshold, cancellationToken);

        var unconfirmedItems = await _context.Assets.AsNoTracking()
            .Where(a => (scope == null || a.CompanyId == null || a.CompanyId == scope.Value)
                        && !a.IsConfirmed)
            .OrderByDescending(a => a.CreatedAt)
            .Take(100)
            .Select(a => new DashboardAlertItemDto(
                a.Id, a.Name, a.AssetTag, null, a.Status.ToString()))
            .ToListAsync(cancellationToken);
        var unconfirmedCount = await _context.Assets.AsNoTracking()
            .CountAsync(a => (scope == null || a.CompanyId == null || a.CompanyId == scope.Value)
                             && !a.IsConfirmed, cancellationToken);

        var campaigns = await _context.MaintenanceCampaigns.AsNoTracking()
            .Where(c => c.Status == MaintenanceCampaignStatus.InProgress
                        && (scope == null || c.CompanyId == null || c.CompanyId == scope.Value))
            .Include(c => c.SystemInfo)
            .Include(c => c.TemplateVersion).ThenInclude(v => v.Template)
            .Include(c => c.TemplateVersion).ThenInclude(v => v.Items).ThenInclude(i => i.StandardParams)
            .Include(c => c.DeviceSnapshots)
            .Include(c => c.Results)
            .OrderByDescending(c => c.StartDate)
            .Take(20)
            .ToListAsync(cancellationToken);

        var activeCampaigns = campaigns.Select(c =>
        {
            var criteriaPerDevice = c.TemplateVersion.Items
                .Sum(i => Math.Max(1, i.StandardParams.Count));
            var total = c.DeviceSnapshots.Count * criteriaPerDevice;
            return new DashboardCampaignDto(
                c.Id,
                c.TemplateVersion.Template.Name,
                c.SystemInfo.Name,
                c.Results.Count,
                total,
                c.EndDate);
        }).ToList();

        var alerts = new List<DashboardAlertDto>
        {
            new("lowStock", summary.LowStockCount, lowStock.Select(x => new DashboardAlertItemDto(
                x.Id, x.Name, null, null, $"{x.Remaining}/{x.MinAmt}")).ToList()),
            new("overdueMaintenance", summary.SystemsOverdueMaintenance, maintenanceItems),
            new("overdueAudits", summary.OverdueAudits, auditItems),
            new("expiringLicenses", licenseCount, licenseItems),
            new("unconfirmedAssets", unconfirmedCount, unconfirmedItems),
        };

        return new DashboardOverviewDto(
            alerts,
            new DashboardNumbersDto(
                totalAssets,
                deployedAssets,
                pendingAssets,
                summary.TotalAssetValue),
            activeCampaigns,
            byStatus,
            byCategory,
            recent,
            now);
    }
}
