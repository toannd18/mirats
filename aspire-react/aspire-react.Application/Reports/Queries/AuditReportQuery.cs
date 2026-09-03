using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Reports.Queries;

public record AuditReportDto(int TotalAudited, int NotAudited, int OverdueAudit);

/// <summary>
/// [Giai đoạn 3] GET /api/v1/reports/audit (extracted from ReportsController.AuditReport).
/// Company-scoped audit counters (audited / not-audited / overdue non-archived) — verbatim.
/// </summary>
public record AuditReportQuery : IRequest<AuditReportDto>;

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
        var audited = await _context.Assets.AsNoTracking()
            .Where(a => (userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value) && a.LastAuditDate != null)
            .CountAsync(cancellationToken);
        var notAudited = await _context.Assets.AsNoTracking()
            .Where(a => (userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value) && a.LastAuditDate == null)
            .CountAsync(cancellationToken);
        var overdue = await _context.Assets.AsNoTracking()
                .Where(a => (userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value)
                            && a.NextAuditDate != null && a.NextAuditDate < now && a.Status != AssetStatus.Archived)
                .CountAsync(cancellationToken);

        return new AuditReportDto(audited, notAudited, overdue);
    }
}
