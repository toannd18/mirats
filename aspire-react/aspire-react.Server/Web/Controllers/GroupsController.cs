using System.Security.Claims;
using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Authorization;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Web.Controllers;

[ApiController]
[Route("api/v1/groups")]
[Authorize(Policy = "admin")]
public class GroupsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IActionLogService _actionLogService;
    private readonly PermissionLockoutGuard _lockoutGuard;

    public GroupsController(AppDbContext context, IActionLogService actionLogService, PermissionLockoutGuard lockoutGuard)
    {
        _context = context;
        _actionLogService = actionLogService;
        _lockoutGuard = lockoutGuard;
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
        var groups = await _context.PermissionGroups
            .Include(g => g.GroupPermissions)
            .Include(g => g.UserGroups).ThenInclude(ug => ug.User)
            .AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new
            {
                g.Id,
                g.Name,
                g.Description,
                g.IsSystem,
                g.CreatedAt,
                g.UpdatedAt,
                Permissions = g.GroupPermissions.Select(p => new { p.PermissionKey, Value = (int)p.Value }),
                UserCount = g.UserGroups.Count
            })
            .ToListAsync();

        return Ok(new { status = "success", data = groups });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetGroup(Guid id)
    {
        var group = await _context.PermissionGroups
            .Include(g => g.GroupPermissions)
            .Include(g => g.UserGroups).ThenInclude(ug => ug.User)
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == id);

        if (group == null)
            return NotFound(new { status = "error", message = "Group not found." });

        return Ok(new
        {
            status = "success",
            data = new
            {
                group.Id,
                group.Name,
                group.Description,
                group.IsSystem,
                Permissions = group.GroupPermissions.Select(p => new { p.PermissionKey, Value = (int)p.Value }),
                Users = group.UserGroups.Select(ug => new
                {
                    ug.User.Id,
                    ug.User.Username,
                    ug.User.Email,
                    ug.User.FirstName,
                    ug.User.LastName
                })
            }
        });
    }

    [HttpPost]
    public async Task<IActionResult> CreateGroup([FromBody] CreateGroupRequest request)
    {
        var group = new PermissionGroup
        {
            Name = request.Name,
            Description = request.Description
        };

        _context.PermissionGroups.Add(group);

        _actionLogService.LogAction(
            itemType: ItemType.PermissionGroup,
            itemId: group.Id,
            actionType: ActionType.Create,
            loggedByUserId: GetCurrentUserId(),
            companyId: null, // PermissionGroup has no CompanyId — company-independent log
            note: $"Created group: {group.Name}");

        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetGroup), new { id = group.Id }, new
        {
            status = "success",
            message = "Group created successfully.",
            data = new { group.Id, group.Name, group.IsSystem }
        });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateGroup(Guid id, [FromBody] UpdateGroupRequest request)
    {
        var group = await _context.PermissionGroups.FindAsync(id);
        if (group == null)
            return NotFound(new { status = "error", message = "Group not found." });

        if (group.IsSystem)
            return BadRequest(new { status = "error", message = "System groups cannot be renamed.", errorCode = "SYSTEM_GROUP_LOCKED" });

        var oldName = group.Name;
        var oldDescription = group.Description;
        group.Name = request.Name;
        group.Description = request.Description;

        _actionLogService.LogAction(
            itemType: ItemType.PermissionGroup,
            itemId: id,
            actionType: ActionType.Update,
            loggedByUserId: GetCurrentUserId(),
            companyId: null, // PermissionGroup has no CompanyId — company-independent log
            note: "Group updated.",
            logMeta: JsonSerializer.Serialize(new
            {
                changes = new
                {
                    name = new { old = oldName, @new = group.Name },
                    description = new { old = oldDescription, @new = group.Description }
                }
            }));

        await _context.SaveChangesAsync();

        return Ok(new { status = "success", message = "Group updated successfully." });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteGroup(Guid id)
    {
        var group = await _context.PermissionGroups.FindAsync(id);
        if (group == null)
            return NotFound(new { status = "error", message = "Group not found." });

        if (group.IsSystem)
            return BadRequest(new { status = "error", message = "System groups cannot be deleted.", errorCode = "SYSTEM_GROUP_LOCKED" });

        // Anti self-lockout: deleting a group that is the only remaining source of admin capability
        // (for any user, not just the actor) would leave the system unmanageable.
        if (await _lockoutGuard.WouldDeleteGroupLockoutAsync(GetCurrentUserId(), id, IsRealmSuperUser()))
            return BadRequest(new
            {
                status = "error",
                message = "Bạn không thể xóa nhóm này khi nó là nguồn cấp quyền quản trị duy nhất còn lại của hệ thống.",
                errorCode = "SELF_LOCKOUT"
            });

        // Log before removal so the audit trail retains the group data.
        _actionLogService.LogAction(
            itemType: ItemType.PermissionGroup,
            itemId: id,
            actionType: ActionType.Delete,
            loggedByUserId: GetCurrentUserId(),
            companyId: null, // PermissionGroup has no CompanyId — company-independent log
            note: $"Deleted group: {group.Name}");

        _context.PermissionGroups.Remove(group);
        await _context.SaveChangesAsync();

        return Ok(new { status = "success", message = "Group deleted successfully." });
    }

    [HttpPut("{id:guid}/permissions")]
    public async Task<IActionResult> UpdateGroupPermissions(Guid id, [FromBody] List<PermissionEntry> permissions)
    {
        var group = await _context.PermissionGroups
            .Include(g => g.GroupPermissions)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (group == null)
            return NotFound(new { status = "error", message = "Group not found." });

        var drafts = permissions
            .Select(p => new GroupPermissionDraft(p.PermissionKey, p.Value))
            .ToList();

        // Anti self-lockout: removing the management permissions (admin/users.edit) from the
        // last group that grants them to the acting admin would leave the system unmanageable.
        if (await _lockoutGuard.WouldGroupPermissionEditLockoutAsync(GetCurrentUserId(), id, drafts, IsRealmSuperUser()))
            return BadRequest(new
            {
                status = "error",
                message = "Bạn không thể gỡ quyền quản trị khỏi nhóm này khi bạn là người cuối cùng còn giữ quyền quản trị.",
                errorCode = "SELF_LOCKOUT"
            });

        var oldPermissions = group.GroupPermissions
            .Select(p => new { p.PermissionKey, Value = (int)p.Value })
            .ToList();

        _context.GroupPermissions.RemoveRange(group.GroupPermissions);

        foreach (var perm in permissions)
        {
            _context.GroupPermissions.Add(new GroupPermission
            {
                GroupId = id,
                PermissionKey = perm.PermissionKey,
                Value = perm.Value
            });
        }

        _actionLogService.LogAction(
            itemType: ItemType.PermissionGroup,
            itemId: id,
            actionType: ActionType.Update,
            loggedByUserId: GetCurrentUserId(),
            companyId: null, // PermissionGroup has no CompanyId — company-independent log
            note: $"Group permissions updated: {group.Name}",
            logMeta: JsonSerializer.Serialize(new
            {
                changes = new
                {
                    permissions = new
                    {
                        old = oldPermissions,
                        @new = permissions.Select(p => new { p.PermissionKey, p.Value })
                    }
                }
            }));

        await _context.SaveChangesAsync();

        return Ok(new { status = "success", message = "Group permissions updated." });
    }
}

public record CreateGroupRequest(string Name, string? Description);
public record UpdateGroupRequest(string Name, string? Description);
public record PermissionEntry(string PermissionKey, Domain.Enums.PermissionValue Value);