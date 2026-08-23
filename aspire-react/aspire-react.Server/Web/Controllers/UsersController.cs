using System.Security.Claims;
using System.Text.Json;
using aspire_react.Server.Application.Users.Commands;
using aspire_react.Server.Application.Users.DTOs;
using aspire_react.Server.Application.Users.Queries;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Authorization;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Web.Controllers;

[ApiController]
[Route("api/v1/users")]
public class UsersController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly AppDbContext _context;
    private readonly IActionLogService _actionLogService;
    private readonly PermissionLockoutGuard _lockoutGuard;
    private readonly ICompanyScopeService _companyScope;

    public UsersController(
        IMediator mediator,
        AppDbContext context,
        IActionLogService actionLogService,
        PermissionLockoutGuard lockoutGuard,
        ICompanyScopeService companyScope)
    {
        _mediator = mediator;
        _context = context;
        _actionLogService = actionLogService;
        _lockoutGuard = lockoutGuard;
        _companyScope = companyScope;
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

    /// <summary>
    /// Returns a paginated list of users with navigation names.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "users.view")]
    public async Task<IActionResult> GetUsers(
        [FromQuery] string? search,
        [FromQuery] Guid? companyId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var query = _context.Users
            .Include(u => u.Company)
            .Include(u => u.Department)
            .Include(u => u.Location)
            .Include(u => u.UserGroups).ThenInclude(ug => ug.Group)
            .AsNoTracking();

        // [Task K] Company-scoping: a regular user only sees users of their own company (or
        // company-less floater). The client-supplied `companyId` is IGNORED for a regular user —
        // scope is forced from the acting user, never from an optional query param. Superuser
        // (GetCurrentUserCompanyIdAsync → null) may optionally filter by `companyId`.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue)
            query = query.Where(u => u.CompanyId == null || u.CompanyId == userCompanyId.Value);
        else if (companyId.HasValue)
            query = query.Where(u => u.CompanyId == companyId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            query = query.Where(u =>
                u.Username.ToLower().Contains(searchLower) ||
                u.Email.ToLower().Contains(searchLower) ||
                u.FirstName.ToLower().Contains(searchLower) ||
                u.LastName.ToLower().Contains(searchLower) ||
                (u.JobTitle != null && u.JobTitle.ToLower().Contains(searchLower)));
        }

        var total = await query.CountAsync();

        var users = await query
            .OrderBy(u => u.Username)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserDto
            {
                Id = u.Id,
                Username = u.Username,
                Email = u.Email,
                FirstName = u.FirstName,
                LastName = u.LastName,
                EmployeeNumber = u.EmployeeNumber,
                JobTitle = u.JobTitle,
                IsSuperUser = u.IsSuperUser,
                IsActive = u.IsActive,
                CompanyId = u.CompanyId,
                CompanyName = u.Company != null ? u.Company.Name : null,
                DepartmentId = u.DepartmentId,
                DepartmentName = u.Department != null ? u.Department.Name : null,
                LocationId = u.LocationId,
                LocationName = u.Location != null ? u.Location.Name : null,
                Groups = u.UserGroups.Select(ug => new UserGroupDto(ug.GroupId, ug.Group.Name, ug.Group.IsSystem)).ToList(),
                CreatedAt = u.CreatedAt,
                UpdatedAt = u.UpdatedAt,
            })
            .ToListAsync();

        return Ok(new
        {
            status = "success",
            data = users,
            pagination = new
            {
                page,
                pageSize,
                totalItems = total,
                totalPages = (int)Math.Ceiling((double)total / pageSize),
                hasNextPage = page * pageSize < total,
                hasPreviousPage = page > 1
            }
        });
    }

    /// <summary>
    /// Returns the currently authenticated user's profile.
    /// Auto-creates local user record from Keycloak claims if not found.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetCurrentUser()
    {
        // Prefer local_user_id claim (stamped by JIT provisioning) — robust against username renames.
        User? user = null;
        if (Guid.TryParse(User.FindFirstValue("local_user_id"), out var localUserId) && localUserId != Guid.Empty)
        {
            user = await _context.Users
                .Include(u => u.Company)
                .Include(u => u.Department)
                .Include(u => u.Location)
                .Include(u => u.UserGroups).ThenInclude(ug => ug.Group)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == localUserId);
        }

        var username = User.FindFirstValue("preferred_username")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(username))
            return Unauthorized(new { status = "error", message = "User not authenticated." });

        if (user == null)
        {
            user = await _context.Users
                .Include(u => u.Company)
                .Include(u => u.Department)
                .Include(u => u.Location)
                .Include(u => u.UserGroups).ThenInclude(ug => ug.Group)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username == username);
        }

        if (user == null)
        {
            // No auto-create here: JIT provisioning (Program.cs → OnTokenValidated) already creates the
            // local user during token validation, before authorization and this endpoint run. Writing to
            // the DB here would be a duplicate side-effect (same class of bug removed from
            // PermissionHandler in Subtask A) — fail closed instead.
            return Unauthorized(new { status = "error", message = "User not found. Please re-authenticate." });
        }

        return Ok(new
        {
            status = "success",
            data = new UserDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                EmployeeNumber = user.EmployeeNumber,
                JobTitle = user.JobTitle,
                IsSuperUser = user.IsSuperUser,
                IsActive = user.IsActive,
                CompanyId = user.CompanyId,
                CompanyName = user.Company?.Name,
                DepartmentId = user.DepartmentId,
                DepartmentName = user.Department?.Name,
                LocationId = user.LocationId,
                LocationName = user.Location?.Name,
                Groups = user.UserGroups.Select(ug => new UserGroupDto(ug.GroupId, ug.Group.Name, ug.Group.IsSystem)).ToList(),
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt,
            }
        });
    }

    /// <summary>
    /// Returns a single user by ID with all navigation data.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = "users.view")]
    public async Task<IActionResult> GetUser(Guid id)
    {
        var user = await _context.Users
            .Include(u => u.Company)
            .Include(u => u.Department)
            .Include(u => u.Location)
            .Include(u => u.UserPermissions)
            .Include(u => u.UserGroups).ThenInclude(ug => ug.Group)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id);

        // [Task K] Company-scoping: a regular user may only view users of their own company (or
        // company-less floater). Out-of-scope → 404 to hide existence (same convention as Task I).
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (user == null || (userCompanyId.HasValue && user.CompanyId.HasValue && user.CompanyId.Value != userCompanyId.Value))
            return NotFound(new { status = "error", message = "User not found." });

        return Ok(new
        {
            status = "success",
            data = new
            {
                user.Id,
                user.Username,
                user.Email,
                user.FirstName,
                user.LastName,
                user.EmployeeNumber,
                user.JobTitle,
                user.IsSuperUser,
                user.IsActive,
                CompanyId = user.CompanyId,
                CompanyName = user.Company?.Name,
                DepartmentId = user.DepartmentId,
                DepartmentName = user.Department?.Name,
                LocationId = user.LocationId,
                LocationName = user.Location?.Name,
                Permissions = user.UserPermissions.Select(p => new { p.PermissionKey, p.Value }),
                Groups = user.UserGroups.Select(ug => new { ug.GroupId, ug.Group.Name }),
                user.CreatedAt,
                user.UpdatedAt,
            }
        });
    }

    /// <summary>
    /// Assigns permission groups to a user (replaces the full set).
    /// Sensitive operation — protected by the "admin" policy and guarded against
    /// self-lockout (an admin who is the last one with permission-management capability
    /// cannot strip their own access).
    /// </summary>
    [HttpPut("{id:guid}/groups")]
    [Authorize(Policy = "admin")]
    public async Task<IActionResult> UpdateUserGroups(Guid id, [FromBody] UpdateUserGroupsRequest request)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
            return NotFound(new { status = "error", message = "User not found." });

        // [SEC-FIX CS-3, 2026-08-23] Company-scoping: a company admin may only manage groups of
        // users in their own company (or floater); Superuser (GetCurrentUserCompanyIdAsync → null)
        // is unrestricted. Mirrors the exact pattern of UpdateUser/DeleteUser above. Previously a
        // company admin could assign ANY group (incl. Admin) to a user of ANOTHER company (verified
        // empirically: cross-company PUT returned 200) while the same target was filtered out of
        // GET /users — read was scoped, write was not. Out-of-scope → 404 (hide existence).
        var actorCompanyScope = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (actorCompanyScope.HasValue && user.CompanyId.HasValue && user.CompanyId.Value != actorCompanyScope.Value)
            return NotFound(new { status = "error", message = "User not found." });

        var requestedIds = (request.GroupIds ?? new List<Guid>()).Distinct().ToList();
        var validGroupIds = await _context.PermissionGroups
            .Where(g => requestedIds.Contains(g.Id))
            .Select(g => g.Id)
            .ToListAsync();

        if (validGroupIds.Count != requestedIds.Count)
            return BadRequest(new
            {
                status = "error",
                message = "One or more groups do not exist.",
                errorCode = "GROUP_NOT_FOUND"
            });

        var actorId = GetCurrentUserId();
        var isRealmSuper = IsRealmSuperUser();

        // Anti self-lockout: the acting admin removing their own last permission-management
        // capability while no other admin remains would leave the system unmanageable.
        if (await _lockoutGuard.WouldSelfAssignLockoutAsync(actorId, id, validGroupIds, isRealmSuper))
            return BadRequest(new
            {
                status = "error",
                message = "Bạn không thể tự gỡ quyền quản trị của chính mình khi bạn là người cuối cùng còn giữ quyền quản trị.",
                errorCode = "SELF_LOCKOUT"
            });

        // Capture old assignment for the audit trail.
        var oldGroupIds = await _context.UserGroups
            .Where(ug => ug.UserId == id)
            .Select(ug => ug.GroupId)
            .ToListAsync();

        _context.UserGroups.RemoveRange(_context.UserGroups.Where(ug => ug.UserId == id));
        foreach (var groupId in validGroupIds)
        {
            _context.UserGroups.Add(new UserGroup { UserId = id, GroupId = groupId });
        }

        // LogAction chỉ add vào change tracker → phải gọi TRƯỚC SaveChanges để được persist.
        _actionLogService.LogAction(
            itemType: ItemType.User,
            itemId: id,
            actionType: ActionType.Update,
            loggedByUserId: actorId,
            companyId: user?.CompanyId,
            note: "User group assignments updated.",
            logMeta: JsonSerializer.Serialize(new
            {
                changes = new
                {
                    groupIds = new
                    {
                        old = oldGroupIds,
                        @new = validGroupIds
                    }
                }
            }));

        await _context.SaveChangesAsync();

        var groups = await _context.PermissionGroups
            .Where(g => validGroupIds.Contains(g.Id))
            .Select(g => new UserGroupDto(g.Id, g.Name, g.IsSystem))
            .ToListAsync();

        return Ok(new
        {
            status = "success",
            message = "User groups updated.",
            data = new
            {
                user.Id,
                user.Username,
                Groups = groups
            }
        });
    }

    /// <summary>
    /// Creates a new user. Syncs one-way to Keycloak before saving to local DB.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "users.create")]
    public async Task<IActionResult> CreateUser(
        [FromBody] CreateUserCommand command,
        [FromServices] IValidator<CreateUserCommand> validator)
    {
        var validationResult = await validator.ValidateAsync(command);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

            return BadRequest(new
            {
                status = "error",
                message = "Validation failed.",
                errors
            });
        }

        var result = await _mediator.Send(command);

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                "VALIDATION_ERROR" => BadRequest(new { status = "error", message = result.Message }),
                "KEYCLOAK_USERNAME_EXISTS" => Conflict(new { status = "error", message = result.Message, errorCode = result.ErrorCode }),
                "KEYCLOAK_EMAIL_EXISTS" => Conflict(new { status = "error", message = result.Message, errorCode = result.ErrorCode }),
                _ => StatusCode(502, new { status = "error", message = result.Message, errorCode = result.ErrorCode })
            };
        }

        // Audit trail (per F10) — log the user-creation action.
        _actionLogService.LogAction(
            itemType: ItemType.User,
            itemId: result.User!.Id,
            actionType: ActionType.Create,
            loggedByUserId: GetCurrentUserId(),
            companyId: result.User?.CompanyId,
            note: $"Tạo người dùng \"{result.User.Username}\"");
        // LogAction only stages the log in the change tracker → must SaveChanges to persist it.
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetUser), new { id = result.User!.Id }, new
        {
            status = "success",
            message = result.Message,
            data = result.User
        });
    }

    /// <summary>
    /// Updates an existing user. Syncs changes one-way to Keycloak.
    /// Handles IsSuperUser toggle for group membership.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = "admin")]
    public async Task<IActionResult> UpdateUser(
        Guid id,
        [FromBody] UpdateUserCommand command,
        [FromServices] IValidator<UpdateUserCommand> validator)
    {
        if (id != command.Id)
            return BadRequest(new { status = "error", message = "ID mismatch." });

        var validationResult = await validator.ValidateAsync(command);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

            return BadRequest(new
            {
                status = "error",
                message = "Validation failed.",
                errors
            });
        }

        // [Task J] Company-scoping: a regular user may only update users of their own company
        // (or floater); Superuser (GetCurrentUserCompanyIdAsync → null) is unrestricted.
        var targetUser = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (targetUser == null)
            return NotFound(new { status = "error", message = "User not found." });

        var actorCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (actorCompanyId.HasValue && targetUser.CompanyId.HasValue && targetUser.CompanyId.Value != actorCompanyId.Value)
            return NotFound(new { status = "error", message = "User not found." });

        // [Task J] Anti self-lockout: demoting the last superuser (no other superuser/admin would
        // remain able to manage permissions) must be blocked — regardless of who performs it.
        if (command.IsSuperUser == false && targetUser.IsSuperUser)
        {
            if (await _lockoutGuard.WouldDemoteSuperUserLockoutAsync(GetCurrentUserId(), id, IsRealmSuperUser()))
                return BadRequest(new
                {
                    status = "error",
                    message = "Bạn không thể hạ quyền superuser khi người này là superuser cuối cùng còn giữ quyền quản trị.",
                    errorCode = "SELF_LOCKOUT"
                });
        }

        var result = await _mediator.Send(command);

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                "USER_NOT_FOUND" => NotFound(new { status = "error", message = result.Message }),
                "KEYCLOAK_SYNC_FAILED" => StatusCode(502, new { status = "error", message = result.Message, errorCode = result.ErrorCode }),
                _ => BadRequest(new { status = "error", message = result.Message, errorCode = result.ErrorCode })
            };
        }

        // Audit trail (per F10) — log the user-update action.
        _actionLogService.LogAction(
            itemType: ItemType.User,
            itemId: command.Id,
            actionType: ActionType.Update,
            loggedByUserId: GetCurrentUserId(),
            companyId: result.User?.CompanyId,
            note: $"Cập nhật người dùng \"{result.User!.Username}\"");
        // LogAction only stages the log in the change tracker → must SaveChanges to persist it.
        await _context.SaveChangesAsync();

        return Ok(new
        {
            status = "success",
            message = result.Message,
            data = result.User
        });
    }

    /// <summary>
    /// Deactivates a user (soft delete). Syncs disable to Keycloak.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "users.delete")]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        // [Task J] Company-scoping + lockout guard need the target's company & superuser flag first.
        var targetUser = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (targetUser == null)
            return NotFound(new { status = "error", message = "User not found." });

        // Company-scoping: a regular user may only deactivate users of their own company (or floater);
        // Superuser (GetCurrentUserCompanyIdAsync → null) is unrestricted.
        var actorCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (actorCompanyId.HasValue && targetUser.CompanyId.HasValue && targetUser.CompanyId.Value != actorCompanyId.Value)
            return NotFound(new { status = "error", message = "User not found." });

        // Anti self-lockout: deactivating the last holder of management capability (superuser or
        // admin) must be blocked — regardless of who performs it.
        if (await _lockoutGuard.WouldDeactivateUserLockoutAsync(GetCurrentUserId(), id, IsRealmSuperUser()))
            return BadRequest(new
            {
                status = "error",
                message = "Bạn không thể vô hiệu hóa người này khi họ là người cuối cùng còn giữ quyền quản trị.",
                errorCode = "SELF_LOCKOUT"
            });

        var result = await _mediator.Send(new DeleteUserCommand(id));

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                "USER_NOT_FOUND" => NotFound(new { status = "error", message = result.Message }),
                _ => BadRequest(new { status = "error", message = result.Message })
            };
        }

        // Audit trail (per F10) — log the user deactivation.
        _actionLogService.LogAction(
            itemType: ItemType.User,
            itemId: id,
            actionType: ActionType.Delete,
            loggedByUserId: GetCurrentUserId(),
            companyId: targetUser.CompanyId,
            note: $"Vô hiệu hóa người dùng (ID {id})");
        // LogAction only stages the log in the change tracker → must SaveChanges to persist it.
        await _context.SaveChangesAsync();

        return Ok(new { status = "success", message = result.Message });
    }
}

public record UpdateUserGroupsRequest(List<Guid> GroupIds);