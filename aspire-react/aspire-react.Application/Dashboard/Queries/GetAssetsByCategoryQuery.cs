using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Dashboard.Queries;

public record AssetsByCategoryItemDto(string Category, string? Color, int Count);

/// <summary>
/// [Giai đoạn 2-cuối] GET /api/v1/dashboard/assets-by-category (extracted verbatim from
/// DashboardController.GetAssetsByCategory). Company-scoped, excludes Archived and assets
/// without Model→Category, grouped by Category name + TagColor.
/// </summary>
public record GetAssetsByCategoryQuery : IRequest<IReadOnlyList<AssetsByCategoryItemDto>>;

public class GetAssetsByCategoryQueryHandler : IRequestHandler<GetAssetsByCategoryQuery, IReadOnlyList<AssetsByCategoryItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetAssetsByCategoryQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<IReadOnlyList<AssetsByCategoryItemDto>> Handle(GetAssetsByCategoryQuery request, CancellationToken cancellationToken)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var data = await _context.Assets
            .AsNoTracking()
            .Where(a => (userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value)
                        && a.Status != AssetStatus.Archived && a.Model != null && a.Model.Category != null)
            .GroupBy(a => new { a.Model!.Category!.Name, a.Model.Category.TagColor })
            .Select(g => new AssetsByCategoryItemDto(g.Key.Name, g.Key.TagColor, g.Count()))
            .ToListAsync(cancellationToken);

        return data;
    }
}
