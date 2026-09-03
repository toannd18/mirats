using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Reports.Queries;

public record DepreciationRowDto(
    Guid Id,
    string AssetTag,
    string Name,
    decimal? PurchaseCost,
    DateTime? PurchaseDate,
    string Model,
    string Depreciation,
    int MonthsTotal,
    int MonthsUsed,
    int MonthsRemaining,
    decimal CurrentBookValue);

/// <summary>
/// [Giai đoạn 3] GET /api/v1/reports/depreciation (extracted from
/// ReportsController.DepreciationReport). Company-scoped straight-line depreciation math
/// (30.44-day months, clamp to total, book value floor 0, round 2) — VERBATIM. Take(200).
/// </summary>
public record DepreciationReportQuery : IRequest<IReadOnlyList<DepreciationRowDto>>;

public class DepreciationReportQueryHandler : IRequestHandler<DepreciationReportQuery, IReadOnlyList<DepreciationRowDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public DepreciationReportQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<IReadOnlyList<DepreciationRowDto>> Handle(DepreciationReportQuery request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var assets = await _context.Assets
            .Include(a => a.Model).ThenInclude(m => m != null ? m.Depreciation : null)
            .AsNoTracking()
            .Where(a => (userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value)
                        && a.Model != null && a.Model.Depreciation != null && a.PurchaseCost.HasValue && a.PurchaseDate.HasValue)
            .Take(200)
            .ToListAsync(cancellationToken);

        var data = assets.Select(a =>
        {
            var months = a.Model!.Depreciation!.Months;
            var monthsUsed = (int)((now - a.PurchaseDate!.Value).TotalDays / 30.44);
            var monthlyDep = a.PurchaseCost!.Value / months;
            var bookValue = Math.Max(0, a.PurchaseCost.Value - (monthlyDep * Math.Min(monthsUsed, months)));
            return new DepreciationRowDto(
                a.Id,
                a.AssetTag,
                a.Name,
                a.PurchaseCost,
                a.PurchaseDate,
                a.Model.Name,
                a.Model.Depreciation.Name,
                months,
                Math.Min(monthsUsed, months),
                Math.Max(0, months - Math.Min(monthsUsed, months)),
                Math.Round(bookValue, 2));
        }).OrderBy(a => a.AssetTag).ToList();

        return data;
    }
}
