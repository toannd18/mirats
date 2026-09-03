using aspire_react.Server.Domain.Authorization;
using MediatR;

namespace aspire_react.Server.Application.Permissions.Queries;

public record CatalogPermissionDto(string Code, string Action, string Description);

public record CatalogResourceDto(string Resource, IReadOnlyList<CatalogPermissionDto> Permissions);

/// <summary>
/// [Giai đoạn 3] GET /api/v1/permissions (extracted from PermissionsController.GetPermissions).
/// Static permission catalog grouped by Resource — NO database, NO company-scoping, NO log,
/// NO invalidation (the [OutputCache(RefData)] attribute stays on the controller action; the
/// catalog is in-code static data that never changes at runtime → nothing to evict).
/// PermissionCatalog moved verbatim to Domain/Authorization so Application can consume it.
/// </summary>
public record ListPermissionsQuery : IRequest<IReadOnlyList<CatalogResourceDto>>;

public class ListPermissionsQueryHandler : IRequestHandler<ListPermissionsQuery, IReadOnlyList<CatalogResourceDto>>
{
    public Task<IReadOnlyList<CatalogResourceDto>> Handle(ListPermissionsQuery request, CancellationToken cancellationToken)
    {
        var data = PermissionCatalog.All
            .GroupBy(p => p.Resource)
            .OrderBy(g => g.Key)
            .Select(g => new CatalogResourceDto(
                g.Key,
                g.OrderBy(p => p.Code)
                    .Select(p => new CatalogPermissionDto(p.Code, p.Action, p.Description))
                    .ToList()))
            .ToList();

        return Task.FromResult<IReadOnlyList<CatalogResourceDto>>(data);
    }
}
