using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Permissions.Queries;

public record EffectivePermissionsDto(IReadOnlyDictionary<string, int> Permissions, bool IsSuperUser, bool IsAdmin);

/// <summary>
/// [Giai đoạn 3] GET /api/v1/permissions/check (extracted from PermissionsController.CheckPermissions).
/// THE permission-resolution path the frontend usePermission hook calls every session —
/// security-critical. Merge logic VERBATIM: DB superuser flag → realm role → UserPermissions
/// (int values) → GroupPermissions (only set when the key is absent or not already Deny -1 —
/// direct Deny overrides group Grant). User null → EMPTY dictionary + false/false (NOT 404 —
/// pre-migration behavior, unit-covered). The 401 fail-closed for an absent/empty
/// local_user_id claim stays in the CONTROLLER (HttpContext concern).
/// </summary>
public record CheckPermissionsQuery(Guid LocalUserId, bool IsRealmSuperUser) : IRequest<EffectivePermissionsDto>;

public class CheckPermissionsQueryHandler : IRequestHandler<CheckPermissionsQuery, EffectivePermissionsDto>
{
    private readonly IApplicationDbContext _context;

    public CheckPermissionsQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<EffectivePermissionsDto> Handle(CheckPermissionsQuery request, CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .Include(u => u.UserPermissions)
            .Include(u => u.UserGroups).ThenInclude(ug => ug.Group).ThenInclude(g => g.GroupPermissions)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.LocalUserId, cancellationToken);

        if (user == null)
        {
            return new EffectivePermissionsDto(new Dictionary<string, int>(), false, false);
        }

        var permissions = new Dictionary<string, int>();

        // Superuser (DB flag)
        if (user.IsSuperUser)
            permissions["superuser"] = 1;

        // Superuser via Keycloak realm role (mirror PermissionHandler step 1)
        if (request.IsRealmSuperUser)
            permissions["superuser"] = 1;

        // User permissions
        foreach (var up in user.UserPermissions)
            permissions[up.PermissionKey] = (int)up.Value;

        // Group permissions (only set if user doesn't have explicit deny)
        foreach (var ug in user.UserGroups)
        {
            foreach (var gp in ug.Group.GroupPermissions)
            {
                if (!permissions.ContainsKey(gp.PermissionKey) || permissions[gp.PermissionKey] != -1)
                {
                    permissions[gp.PermissionKey] = (int)gp.Value;
                }
            }
        }

        return new EffectivePermissionsDto(
            permissions,
            user.IsSuperUser || request.IsRealmSuperUser,
            permissions.ContainsKey("admin") && permissions["admin"] == 1);
    }
}
