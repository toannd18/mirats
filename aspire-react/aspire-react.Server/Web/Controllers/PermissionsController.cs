using System.Security.Claims;
using aspire_react.Server.Application.Permissions.Queries;
using aspire_react.Server.Infrastructure.Services;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// [Giai đoạn 3] Permissions migrated to MediatR — read-only permission-resolution controller
/// (0 mutation, 0 ActionLog, no Commands → no markers). Routes unchanged: /api/v1/permissions...
/// SECURITY-CRITICAL: /check is the path the frontend usePermission hook calls every session.
/// Parity quirks kept verbatim: [OutputCache(RefData)] on catalog only; absent/empty
/// local_user_id claim → Unauthorized() 401 fail-closed (SEC-FIX CLAIM-CLEANUP); user-null →
/// empty dict + false/false (NOT 404); matrix values are INTs.
/// </summary>
[ApiController]
[Route("api/v1/permissions")]
public class PermissionsController : ControllerBase
{
    private readonly IMediator _mediator;
    public PermissionsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Full permission catalog grouped by resource — single source of truth is
    /// <see cref="PermissionCatalog"/> (Domain/Authorization). Used by the frontend to render the
    /// role (group) permission matrix without hardcoding permission keys.
    /// </summary>
    [HttpGet]
    [Authorize]
    [OutputCache(PolicyName = "RefData")] // Task P: static PermissionCatalog — identical for all authenticated users (NOT /check or /matrix)
    public async Task<IActionResult> GetPermissions()
    {
        var data = await _mediator.Send(new ListPermissionsQuery());
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

        var dto = await _mediator.Send(new CheckPermissionsQuery(localUserId, RealmAccessHelper.IsSuperUser(User)));

        return Ok(new
        {
            status = "success",
            data = new { dto.Permissions, dto.IsSuperUser, dto.IsAdmin }
        });
    }

    [HttpGet("matrix")]
    [Authorize(Policy = "admin")]
    public async Task<IActionResult> GetPermissionMatrix()
    {
        var users = await _mediator.Send(new GetPermissionMatrixQuery());
        return Ok(new { status = "success", data = users });
    }
}
