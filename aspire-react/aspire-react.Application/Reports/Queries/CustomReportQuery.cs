using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Reports.Queries;

public record CustomReportRowDto(
    Guid Id,
    string AssetTag,
    string Name,
    string? Serial,
    decimal? PurchaseCost,
    DateTime? PurchaseDate,
    string Status,
    SystemReportRefDto? Model,
    SystemReportRefDto? Category,
    SystemReportRefDto? Location,
    SystemReportRefDto? Company,
    DateTime CreatedAt);

public record SystemReportRefDto(Guid Id, string Name);

/// <summary>
/// [Giai đoạn 3] GET /api/v1/reports/custom (extracted from ReportsController.CustomReport).
/// Company-scoped asset report with optional filters (dates/category/location/status), Take(500),
/// Status serialized as string (global converter) — verbatim. Read-only: no markers.
/// </summary>
public record CustomReportQuery(
    DateTime? StartDate, DateTime? EndDate, Guid? CategoryId, Guid? LocationId,
    AssetStatus? Status) : IRequest<CustomReportResult>;

public record CustomReportResult(IReadOnlyList<CustomReportRowDto> Items, int Total);

public class CustomReportQueryHandler : IRequestHandler<CustomReportQuery, CustomReportResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public CustomReportQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<CustomReportResult> Handle(CustomReportQuery request, CancellationToken cancellationToken)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var query = _context.Assets.Include(a => a.Model).ThenInclude(m => m != null ? m.Category : null)
            .Include(a => a.Location).Include(a => a.Company)
            .AsNoTracking().AsQueryable();

        query = query.Where(a => userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value);

        if (request.StartDate.HasValue) query = query.Where(a => a.CreatedAt >= request.StartDate.Value);
        if (request.EndDate.HasValue) query = query.Where(a => a.CreatedAt <= request.EndDate.Value);
        if (request.CategoryId.HasValue) query = query.Where(a => a.Model != null && a.Model.CategoryId == request.CategoryId);
        if (request.LocationId.HasValue) query = query.Where(a => a.LocationId == request.LocationId);
        if (request.Status.HasValue) query = query.Where(a => a.Status == request.Status.Value);

        var assets = await query.OrderBy(a => a.AssetTag).Take(500).Select(a => new CustomReportRowDto(
            a.Id,
            a.AssetTag,
            a.Name,
            a.Serial,
            a.PurchaseCost,
            a.PurchaseDate,
            a.Status.ToString(),
            a.Model == null ? null : new SystemReportRefDto(a.Model.Id, a.Model.Name),
            a.Model != null && a.Model.Category != null ? new SystemReportRefDto(a.Model.Category.Id, a.Model.Category.Name) : null,
            a.Location == null ? null : new SystemReportRefDto(a.Location.Id, a.Location.Name),
            a.Company == null ? null : new SystemReportRefDto(a.Company.Id, a.Company.Name),
            a.CreatedAt)).ToListAsync(cancellationToken);

        return new CustomReportResult(assets, assets.Count);
    }
}
