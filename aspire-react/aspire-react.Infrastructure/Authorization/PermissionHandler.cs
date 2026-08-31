using System.Security.Claims;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Infrastructure.Authorization;

public class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly AppDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PermissionHandler(AppDbContext context, IHttpContextAccessor httpContextAccessor)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            context.Fail();
            return;
        }

        // 1. Superuser / Admin bypass via Keycloak realm_access roles (EXACT match — a role merely
        //    containing "admin"/"superuser" as a substring must NOT grant superuser).
        if (RealmAccessHelper.IsSuperUser(context.User))
        {
            context.Succeed(requirement);
            return;
        }

        // 2. Resolve the local user.
        //    ONLY the `local_user_id` claim stamped by JIT provisioning is used — Keycloak
        //    `sub`/`preferred_username` are NEVER a user identity source (bug-class 1). Matching by
        //    username would silently lose permissions on renames/casing; parsing `sub` returns the
        //    wrong id. When the claim is absent, fail closed (context.Fail) — no legacy fallback.
        //    [SEC-FIX CLAIM-CLEANUP, 2026-08-23]
        if (!Guid.TryParse(context.User.FindFirstValue("local_user_id"), out var localUserId)
            || localUserId == Guid.Empty)
        {
            context.Fail();
            return;
        }

        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == localUserId);

        if (user == null)
        {
            // No auto-create here: JIT provisioning already creates the local user during
            // token validation, before authorization runs. Writing to the DB from inside an
            // authorization handler was a duplicate side-effect — fail closed instead.
            context.Fail();
            return;
        }

        // 3. Local admin bypass
        if (user.IsSuperUser)
        {
            context.Succeed(requirement);
            return;
        }

        // 4. Check User explicit permissions
        var userPerm = await _context.UserPermissions
            .AsNoTracking()
            .FirstOrDefaultAsync(up => up.UserId == user.Id && up.PermissionKey == requirement.PermissionKey);

        // 4a. User explicit Deny — override everything
        if (userPerm is { Value: PermissionValue.Deny })
        {
            context.Fail();
            return;
        }

        // 4b. User explicit Grant
        if (userPerm is { Value: PermissionValue.Grant })
        {
            context.Succeed(requirement);
            return;
        }

        // 5. Check Group Grants
        var groupIds = await _context.UserGroups
            .AsNoTracking()
            .Where(ug => ug.UserId == user.Id)
            .Select(ug => ug.GroupId)
            .ToListAsync();

        if (groupIds.Count > 0)
        {
            var hasGroupGrant = await _context.GroupPermissions
                .AsNoTracking()
                .AnyAsync(gp =>
                    groupIds.Contains(gp.GroupId) &&
                    gp.PermissionKey == requirement.PermissionKey &&
                    gp.Value == PermissionValue.Grant);

            if (hasGroupGrant)
            {
                context.Succeed(requirement);
                return;
            }
        }

        // 6. Also check for "admin" permission specifically — acts as a wildcard for all
        //    permissions except "admin" itself (avoids a self-referential check).
        if (requirement.PermissionKey != "admin")
        {
            var hasAdminAccess = await _context.UserPermissions
                .AsNoTracking()
                .AnyAsync(up => up.UserId == user.Id && up.PermissionKey == "admin" && up.Value == PermissionValue.Grant);

            if (!hasAdminAccess && groupIds.Count > 0)
            {
                hasAdminAccess = await _context.GroupPermissions
                    .AsNoTracking()
                    .AnyAsync(gp =>
                        groupIds.Contains(gp.GroupId) &&
                        gp.PermissionKey == "admin" &&
                        gp.Value == PermissionValue.Grant);
            }

            if (hasAdminAccess)
            {
                context.Succeed(requirement);
                return;
            }
        }

        // 7. Default Deny
        context.Fail();
    }
}
