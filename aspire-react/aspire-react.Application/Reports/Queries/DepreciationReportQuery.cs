using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Reports.RepairReports;
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

public record DepreciationReportResult(
    IReadOnlyList<DepreciationRowDto> Items,
    int Total,
    int Page,
    int PageSize,
    bool HasMore);

/// <summary>
/// GET /api/v1/reports/depreciation. Company-scoped straight-line depreciation math
/// (30.44-day months, clamp to total, book value floor 0, round 2). Paginated so the
/// report never silently truncates a large inventory.
/// </summary>
public record DepreciationReportQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? CompanyId = null,
    Guid? CategoryId = null,
    int? Year = null) : IRequest<DepreciationReportResult>;

public class DepreciationReportQueryHandler : IRequestHandler<DepreciationReportQuery, DepreciationReportResult>
{
    private const int MaxPageSize = 100;
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public DepreciationReportQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<DepreciationReportResult> Handle(DepreciationReportQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);
        var now = DateTime.UtcNow;
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();

        var query = _context.Assets
            .Include(a => a.Model).ThenInclude(m => m != null ? m.Depreciation : null)
            .AsNoTracking()
            .Where(a => (userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value)
                        && a.Model != null && a.Model.Depreciation != null
                        && a.PurchaseCost.HasValue && a.PurchaseDate.HasValue);

        if (request.CompanyId.HasValue) query = query.Where(a => a.CompanyId == request.CompanyId);
        if (request.CategoryId.HasValue)
            query = query.Where(a => a.Model != null && a.Model.CategoryId == request.CategoryId);
        if (request.Year.HasValue)
        {
            // assets.PurchaseDate is timestamptz: filter by the VN calendar year, not UTC boundaries.
            var (start, end) = VnReportDate.YearRangeUtc(request.Year.Value);
            query = query.Where(a => a.PurchaseDate >= start && a.PurchaseDate < end);
        }

        var total = await query.CountAsync(cancellationToken);
        var assets = await query
            .OrderBy(a => a.AssetTag)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
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
        }).ToList();

        return new DepreciationReportResult(data, total, page, pageSize, page * pageSize < total);
    }
}
