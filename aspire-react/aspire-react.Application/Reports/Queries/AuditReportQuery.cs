using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Reports.RepairReports;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Reports.Queries;

public record AuditReportDto(int TotalAudited, int NotAudited, int OverdueAudit, int TotalAssets);

/// <summary>
/// GET /api/v1/reports/audit. Company-scoped audit counters with the same optional
/// company/category/year filters exposed by the depreciation report.
/// </summary>
public record AuditReportQuery(
    Guid? CompanyId = null,
    Guid? CategoryId = null,
    int? Year = null) : IRequest<AuditReportDto>;

public class AuditReportQueryHandler : IRequestHandler<AuditReportQuery, AuditReportDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public AuditReportQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<AuditReportDto> Handle(AuditReportQuery request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();

        var query = _context.Assets.AsNoTracking()
            .Where(a => userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value);

        if (request.CompanyId.HasValue) query = query.Where(a => a.CompanyId == request.CompanyId);
        if (request.CategoryId.HasValue)
            query = query.Where(a => a.Model != null && a.Model.CategoryId == request.CategoryId);
        if (request.Year.HasValue)
        {
            // assets.PurchaseDate is timestamptz: filter by the VN calendar year, not UTC boundaries.
            var (start, end) = VnReportDate.YearRangeUtc(request.Year.Value);
            query = query.Where(a => a.PurchaseDate >= start && a.PurchaseDate < end);
        }

        var audited = await query.CountAsync(a => a.LastAuditDate != null, cancellationToken);
        var notAudited = await query.CountAsync(a => a.LastAuditDate == null, cancellationToken);
        var overdue = await query.CountAsync(
            a => a.NextAuditDate != null && a.NextAuditDate < now && a.Status != AssetStatus.Archived,
            cancellationToken);
        var totalAssets = await query.CountAsync(cancellationToken);

        return new AuditReportDto(audited, notAudited, overdue, totalAssets);
    }
}
