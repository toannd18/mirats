using System.Security.Claims;
using aspire_react.Server.Application.Groups.Commands;
using aspire_react.Server.Application.Groups.Queries;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Services;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// [Giai đoạn 3] Groups migrated to MediatR — admin-only permission-group management
/// ([Authorize(Policy = "admin")] class-level, no company-scoping by design).
/// Commands: Create/Update/Delete/UpdateGroupPermissions — ILoggableCommand only (no output-cache);
/// Delete + UpdateGroupPermissions wire IPermissionLockoutGuard (SELF_LOCKOUT) with the
/// realm-superuser flag resolved HERE from HttpContext (handlers cannot read HttpContext).
/// Response/error-shape parity notes (verbatim quirks, see BACKLOG BUG-K convention note):
/// errors use errorCode in CAMELCASE (not error_code); GetGroups Permissions[].Value is INT;
/// Create returns CreatedAtAction 201.
/// </summary>
[ApiController]
[Route("api/v1/groups")]
[Authorize(Policy = "admin")]
public class GroupsController : ControllerBase
{
    private readonly IMediator _mediator;
    public GroupsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    private Guid GetCurrentUserId()
    {
        var claimValue = User?.FindFirstValue("local_user_id") ?? string.Empty;
        return Guid.TryParse(claimValue, out var id) ? id : Guid.Empty;
    }

    /// <summary>
    /// Mirrors <see cref="PermissionHandler"/> step 1: realm_access superuser/admin (substring
    /// on the raw claim JSON) or a "permission" claim "superuser" → full bypass.
    /// </summary>
    private bool IsRealmSuperUser() => User != null && RealmAccessHelper.IsSuperUser(User);

    [HttpGet]
    public async Task<IActionResult> GetGroups()
    {
        var groups = await _mediator.Send(new ListGroupsQuery());
        return Ok(new { status = "success", data = groups });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetGroup(Guid id)
    {
        var group = await _mediator.Send(new GetGroupByIdQuery(id));
        if (group == null)
            return NotFound(new { status = "error", message = "Group not found." });

        return Ok(new { status = "success", data = group });
    }

    [HttpPost]
    public async Task<IActionResult> CreateGroup([FromBody] CreateGroupRequest request)
    {
        var result = await _mediator.Send(new CreateGroupCommand(request.Name, request.Description, GetCurrentUserId()));
        if (!result.Success)
            return MapFailure(result);

        // Verbatim CreatedAtAction → 201 with location header.
        return CreatedAtAction(nameof(GetGroup), new { id = result.GroupId }, new
        {
            status = "success",
            message = result.Message,
            data = new { Id = result.GroupId, Name = result.Name, IsSystem = result.IsSystem }
        });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateGroup(Guid id, [FromBody] UpdateGroupRequest request)
    {
        var result = await _mediator.Send(new UpdateGroupCommand(id, request.Name, request.Description, GetCurrentUserId()));
        if (!result.Success)
            return MapFailure(result);

        return Ok(new { status = "success", message = "Group updated successfully." });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteGroup(Guid id)
    {
        var result = await _mediator.Send(new DeleteGroupCommand(id, GetCurrentUserId(), IsRealmSuperUser()));
        if (!result.Success)
            return MapFailure(result);

        return Ok(new { status = "success", message = "Group deleted successfully." });
    }

    [HttpPut("{id:guid}/permissions")]
    public async Task<IActionResult> UpdateGroupPermissions(Guid id, [FromBody] List<PermissionEntry> permissions)
    {
        var result = await _mediator.Send(new UpdateGroupPermissionsCommand(
            id,
            permissions.Select(p => new GroupPermissionEntry(p.PermissionKey, p.Value)).ToList(),
            GetCurrentUserId(),
            IsRealmSuperUser()));
        if (!result.Success)
            return MapFailure(result);

        return Ok(new { status = "success", message = "Group permissions updated." });
    }

    /// <summary>
    /// Maps a GroupResult failure to the EXACT same HTTP bodies as the pre-migration controller:
    /// NOT_FOUND → 404; SYSTEM_GROUP_LOCKED / SELF_LOCKOUT → 400 with errorCode in CAMELCASE
    /// (verbatim — this controller's error bodies differ from other controllers' error_code).
    /// </summary>
    private IActionResult MapFailure(GroupResult result)
    {
        if (result.ErrorCode == "NOT_FOUND")
            return NotFound(new { status = "error", message = result.Message });

        object body = result.ErrorCode is null
            ? new { status = "error", message = result.Message }
            : new { status = "error", message = result.Message, errorCode = result.ErrorCode };
        return BadRequest(body);
    }
}

/// <summary>Request DTOs — verbatim from the pre-migration records.</summary>
public record CreateGroupRequest(string Name, string? Description);
public record UpdateGroupRequest(string Name, string? Description);
public record PermissionEntry(string PermissionKey, Domain.Enums.PermissionValue Value);
