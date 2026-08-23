using System.Security.Claims;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Infrastructure.Authorization;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Web.Controllers;

[ApiController]
[Route("api/v1/permissions")]
public class PermissionsController : ControllerBase
{
    private readonly AppDbContext _context;

    public PermissionsController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Full permission catalog grouped by resource — single source of truth is
    /// <see cref="PermissionCatalog"/>. Used by the frontend to render the
    /// role (group) permission matrix without hardcoding permission keys.
    /// </summary>
    [HttpGet]
    [Authorize]
    [OutputCache(PolicyName = "RefData")] // Task P: static PermissionCatalog — identical for all authenticated users (NOT /check or /matrix)
    public IActionResult GetPermissions()
    {
        var data = PermissionCatalog.All
            .GroupBy(p => p.Resource)
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                resource = g.Key,
                permissions = g
                    .OrderBy(p => p.Code)
                    .Select(p => new { code = p.Code, action = p.Action, description = p.Description })
            });

        return Ok(new { status = "success", data });
    }

    [HttpGet("check")]
    [Authorize]
    public async Task<IActionResult> CheckPermissions()
    {
        // Resolve the local user — mirror PermissionHandler: ONLY the `local_user_id` claim
        // stamped by JIT provisioning is used (Keycloak `sub`/`preferred_username` are never a
        // user identity source — bug-class 1). No legacy username/sub fallback; absent claim →
        // Unauthorized (fail closed). [SEC-FIX CLAIM-CLEANUP, 2026-08-23]
        if (!Guid.TryParse(User.FindFirstValue("local_user_id"), out var localUserId)
            || localUserId == Guid.Empty)
            return Unauthorized();

        var user = await _context.Users
            .Include(u => u.UserPermissions)
            .Include(u => u.UserGroups).ThenInclude(ug => ug.Group).ThenInclude(g => g.GroupPermissions)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == localUserId);

        if (user == null)
        {
            return Ok(new
            {
                status = "success",
                data = new
                {
                    permissions = new Dictionary<string, int>(),
                    isSuperUser = false,
                    isAdmin = false
                }
            });
        }

        var permissions = new Dictionary<string, int>();

        // Superuser (DB flag)
        if (user.IsSuperUser)
            permissions["superuser"] = 1;

        // Superuser via Keycloak realm role (mirror PermissionHandler step 1)
        var isRealmSuperUser = RealmAccessHelper.IsSuperUser(User);
        if (isRealmSuperUser)
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

        return Ok(new
        {
            status = "success",
            data = new
            {
                permissions,
                isSuperUser = user.IsSuperUser || isRealmSuperUser,
                isAdmin = permissions.ContainsKey("admin") && permissions["admin"] == 1
            }
        });
    }

    [HttpGet("matrix")]
    [Authorize(Policy = "admin")]
    public async Task<IActionResult> GetPermissionMatrix()
    {
        var users = await _context.Users
            .Include(u => u.UserPermissions)
            .Include(u => u.UserGroups).ThenInclude(ug => ug.Group).ThenInclude(g => g.GroupPermissions)
            .AsNoTracking()
            .OrderBy(u => u.Username)
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.Email,
                u.FirstName,
                u.LastName,
                u.IsSuperUser,
                UserPermissions = u.UserPermissions.Select(p => new { p.PermissionKey, Value = (int)p.Value }),
                GroupPermissions = u.UserGroups.SelectMany(ug =>
                    ug.Group.GroupPermissions.Select(gp => new
                    {
                        GroupName = ug.Group.Name,
                        gp.PermissionKey,
                        Value = (int)gp.Value
                    }))
            })
            .ToListAsync();

        return Ok(new { status = "success", data = users });
    }
}