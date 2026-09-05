using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.ImportExport.Queries;

public sealed record ExportAssetRowDto(
    string AssetTag, string Name, string? Serial, string? ModelName, string? CategoryName,
    string Status, string? LocationName, decimal? PurchaseCost, DateTime? PurchaseDate);

public sealed record ExportConsumableRowDto(string Name, string? ItemNo, int Qty, int MinAmt, int Remaining);

/// <summary>
/// [Giai đoạn 3 — ImportExport] GET export/assets data (extracted verbatim from
/// ImportExportController.ExportAssets): scope filter (superuser all / regular floater+own),
/// Include Location + Model.Category, Take(1000) — no OrderBy (verbatim quirk: export order is
/// undefined DB order, preserved as-is). The CONTROLLER renders the ClosedXML workbook + File()
/// filename (Web presentation concern; ClosedXML stays out of Application).
/// </summary>
public record ExportAssetsQuery : IRequest<IReadOnlyList<ExportAssetRowDto>>;

public class ExportAssetsQueryHandler : IRequestHandler<ExportAssetsQuery, IReadOnlyList<ExportAssetRowDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public ExportAssetsQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<IReadOnlyList<ExportAssetRowDto>> Handle(ExportAssetsQuery request, CancellationToken cancellationToken)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var assets = await _context.Assets
            .Include(a => a.Location)
            .Include(a => a.Model).ThenInclude(m => m != null ? m.Category : null)
            .Where(a => userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value)
            .AsNoTracking().Take(1000).ToListAsync(cancellationToken);

        return assets.Select(a => new ExportAssetRowDto(
            a.AssetTag, a.Name, a.Serial, a.Model?.Name, a.Model?.Category?.Name,
            a.Status.ToString(), a.Location?.Name, a.PurchaseCost, a.PurchaseDate)).ToList();
    }
}

/// <summary>
/// [Giai đoạn 3 — ImportExport] GET export/consumables data (extracted verbatim from
/// ImportExportController.ExportConsumables): scope filter + Include(Checkouts) + Take(1000);
/// Remaining = Qty − sum(checkout quantities) computed here so the controller only renders.
/// </summary>
public record ExportConsumablesQuery : IRequest<IReadOnlyList<ExportConsumableRowDto>>;

public class ExportConsumablesQueryHandler : IRequestHandler<ExportConsumablesQuery, IReadOnlyList<ExportConsumableRowDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public ExportConsumablesQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<IReadOnlyList<ExportConsumableRowDto>> Handle(ExportConsumablesQuery request, CancellationToken cancellationToken)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var items = await _context.Consumables.Include(c => c.Checkouts).AsNoTracking()
            .Where(c => userCompanyId == null || c.CompanyId == null || c.CompanyId == userCompanyId.Value)
            .Take(1000).ToListAsync(cancellationToken);

        return items.Select(c => new ExportConsumableRowDto(
            c.Name, c.ItemNo, c.Qty, c.MinAmt, c.Qty - c.Checkouts.Sum(ch => ch.Quantity))).ToList();
    }
}
