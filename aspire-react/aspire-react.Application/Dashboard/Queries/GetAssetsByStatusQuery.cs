using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Dashboard.Queries;

public record AssetsByStatusItemDto(string Status, int Count);

/// <summary>
/// [Giai đoạn 2-cuối] GET /api/v1/dashboard/assets-by-status (extracted verbatim from
/// DashboardController.GetAssetsByStatus). Company-scoped, excludes Archived, grouped by
/// AssetStatus enum (serialized as string — NOT the StatusLabel feature, which was removed).
/// </summary>
public record GetAssetsByStatusQuery : IRequest<IReadOnlyList<AssetsByStatusItemDto>>;

public class GetAssetsByStatusQueryHandler : IRequestHandler<GetAssetsByStatusQuery, IReadOnlyList<AssetsByStatusItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetAssetsByStatusQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<IReadOnlyList<AssetsByStatusItemDto>> Handle(GetAssetsByStatusQuery request, CancellationToken cancellationToken)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var data = await _context.Assets
            .AsNoTracking()
            .Where(a => (userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value)
                        && a.Status != AssetStatus.Archived)
            .GroupBy(a => a.Status)
            .Select(g => new AssetsByStatusItemDto(g.Key.ToString(), g.Count()))
            .ToListAsync(cancellationToken);

        return data;
    }
}
