using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Dashboard.Queries;

public record DashboardSummaryDto(
    int TotalAssets,
    int DeployedAssets,
    int RtdAssets,
    int OverdueAudits,
    int ArchivedAssets,
    int LowStockCount,
    int SystemsOverdueMaintenance,
    decimal TotalAssetValue);

/// <summary>
/// [Giai đoạn 2-cuối] GET /api/v1/dashboard/summary (extracted from DashboardController.GetSummary).
/// Company-scoping verbatim: superuser sees all; regular user sees own company + floater.
/// systemsOverdueMaintenance = MC-4 pattern (NextMaintenanceDueDate in the past).
/// </summary>
public record GetDashboardSummaryQuery : IRequest<DashboardSummaryDto>;

public class GetDashboardSummaryQueryHandler : IRequestHandler<GetDashboardSummaryQuery, DashboardSummaryDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetDashboardSummaryQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<DashboardSummaryDto> Handle(GetDashboardSummaryQuery request, CancellationToken cancellationToken)
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

        var totalAssets = await assets.CountAsync(cancellationToken);
        var deployed = await assets.CountAsync(a => a.CurrentAssignmentId != null, cancellationToken);
        // Pending assets are available (not deployed, not archived)
        var rtd = await assets.CountAsync(a => a.Status == AssetStatus.Pending && a.CurrentAssignmentId == null, cancellationToken);
        var overdueAudits = await assets.CountAsync(a => a.NextAuditDate != null && a.NextAuditDate < now && a.Status != AssetStatus.Archived, cancellationToken);
        var archived = await assets.CountAsync(a => a.Status == AssetStatus.Archived, cancellationToken);

        var totalValue = await assets.SumAsync(a => a.PurchaseCost ?? 0, cancellationToken);

        var lowConsumables = await consumables.CountAsync(c => (c.Qty - c.Checkouts.Sum(ch => (int?)ch.Quantity ?? 0)) <= c.MinAmt, cancellationToken);
        var lowAccessories = await accessories.CountAsync(a => (a.Qty - a.Checkouts.Sum(ch => (int?)(ch.AssignedQty - ch.ReturnedQty) ?? 0)) <= a.MinAmt, cancellationToken);
        var lowComponents = await components.CountAsync(c => (c.Qty - c.Assignments.Sum(a => (int?)a.AssignedQty ?? 0)) <= c.MinAmt, cancellationToken);

        // [MC-4] Systems with an overdue maintenance schedule — same company-scoped count pattern as
        // overdueAudits. A system is "quá hạn" when its next maintenance due date is in the past
        // (NextMaintenanceDueDate is computed at campaign Complete; NULL = never completed → not counted).
        var systemsOverdueMaintenance = await _context.SystemInfos.AsNoTracking()
            .Where(s => userCompanyId == null || s.CompanyId == null || s.CompanyId == userCompanyId.Value)
            .CountAsync(s => s.NextMaintenanceDueDate != null && s.NextMaintenanceDueDate < now, cancellationToken);

        return new DashboardSummaryDto(
            TotalAssets: totalAssets,
            DeployedAssets: deployed,
            RtdAssets: rtd,
            OverdueAudits: overdueAudits,
            ArchivedAssets: archived,
            LowStockCount: lowConsumables + lowAccessories + lowComponents,
            SystemsOverdueMaintenance: systemsOverdueMaintenance,
            TotalAssetValue: totalValue);
    }
}
