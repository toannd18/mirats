using System.Security.Claims;
using aspire_react.Server.Application.Users.Commands;
using aspire_react.Server.Application.Users.Queries;
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
/// <summary>
/// [Giai đoạn 3] Users PARTIAL migration (ranh giới đã duyệt): 4 action inline
/// (List/GetCurrentUser/GetUser/UpdateUserGroups) chuyển sang MediatR Queries/Command;
/// 3 action write (Create/Update/Delete) GIỮ NGUYÊN Command từ M1 — không đụng lại
/// (Keycloak liên quan, giảm thiểu rủi ro). Ctor vẫn hybrid (IMediator + DbContext +
/// ActionLog/lockout/scope cho 3 write) — KHÔNG thu gọn thành IMediator-only như Groups.
/// BUG-M (docs/BACKLOG.md, LOW): 3 write log 2 lần (handler 1 + controller 1), log thứ 2
/// không atomic với data — giữ verbatim, fix riêng sau.
/// UpdateUserGroups sau migrate: ILoggableCommand (behavior commit data+log 1 lần, atomic)
/// + enrichment 2a có chủ đích (RemoteIp/UserAgent/ActionSource — đã duyệt playbook §4).
/// Error-shape parity: UpdateUserGroups dùng errorCode CAMELCASE (verbatim, khác error_code).
/// </summary>
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
    /// [Giai đoạn 3] Thin MediatR mapping over ListUsersQuery (logic verbatim trong handler).
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "users.view")]
    public async Task<IActionResult> GetUsers(
        [FromQuery] string? search,
        [FromQuery] Guid? companyId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _mediator.Send(new ListUsersQuery(search, companyId, page, pageSize));

        return Ok(new
        {
            status = "success",
            data = result.Items,
            pagination = new
            {
                page,
                pageSize,
                totalItems = result.Total,
                totalPages = (int)Math.Ceiling((double)result.Total / pageSize),
                hasNextPage = page * pageSize < result.Total,
                hasPreviousPage = page > 1
            }
        });
    }

    /// <summary>
    /// Returns the currently authenticated user's profile.
    /// Auto-creates local user record from Keycloak claims if not found.
    /// [Giai đoạn 3] Thin MediatR mapping over GetCurrentUserQuery (claim parse + Unauthorized
    /// mapping giữ ở controller — verbatim).
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetCurrentUser()
    {
        // [SEC-FIX CLAIM-CLEANUP, 2026-08-23] ONLY "local_user_id" (stamped by JIT provisioning)
        // is a user identity source — Keycloak sub/preferred_username are never used (bug-class 1;
        // a username lookup would break on renames/casing, and `sub` is the wrong id). Absent
        // claim or unknown local id → Unauthorized (fail closed), no legacy fallback.
        if (!Guid.TryParse(User.FindFirstValue("local_user_id"), out var localUserId) || localUserId == Guid.Empty)
            return Unauthorized(new { status = "error", message = "User not authenticated." });

        var user = await _mediator.Send(new GetCurrentUserQuery(localUserId));

        if (user == null)
            return Unauthorized(new { status = "error", message = "User not authenticated." });

        return Ok(new
        {
            status = "success",
            data = user
        });
    }

    /// <summary>
    /// Returns a single user by ID with all navigation data.
    /// [Giai đoạn 3] Thin MediatR mapping over GetUserByIdQuery (scoping + shape verbatim
    /// trong handler; out-of-scope → 404 hide-existence).
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = "users.view")]
    public async Task<IActionResult> GetUser(Guid id)
    {
        var user = await _mediator.Send(new GetUserByIdQuery(id));

        if (user == null)
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
                CompanyName = user.CompanyName,
                DepartmentId = user.DepartmentId,
                DepartmentName = user.DepartmentName,
                LocationId = user.LocationId,
                LocationName = user.LocationName,
                Permissions = user.Permissions.Select(p => new { p.PermissionKey, p.Value }),
                Groups = user.Groups.Select(g => new { g.GroupId, g.Name }),
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
    /// [Giai đoạn 3] Thin MediatR mapping over UpdateUserGroupsCommand (ILoggableCommand —
    /// scope/guard/log verbatim trong handler; realm-superuser flag resolve ở đây vì handler
    /// không đọc HttpContext). GIỮ NGUYÊN policy "admin" (Task J — không phải users.edit).
    /// </summary>
    [HttpPut("{id:guid}/groups")]
    [Authorize(Policy = "admin")]
    public async Task<IActionResult> UpdateUserGroups(Guid id, [FromBody] UpdateUserGroupsRequest request)
    {
        var result = await _mediator.Send(new UpdateUserGroupsCommand(
            id,
            (IReadOnlyList<Guid>)(request.GroupIds ?? new List<Guid>()),
            GetCurrentUserId(),
            IsRealmSuperUser()));

        if (!result.Success)
            return MapUserGroupsFailure(result);

        return Ok(new
        {
            status = "success",
            message = "User groups updated.",
            data = new
            {
                Id = result.UserId,
                Username = result.Username,
                Groups = result.Groups
            }
        });
    }

    /// <summary>
    /// Maps an UpdateUserGroupsResult failure to the EXACT same HTTP bodies as the pre-migration
    /// controller: NOT_FOUND → 404 without errorCode (hide-existence, incl. company-scope);
    /// GROUP_NOT_FOUND / SELF_LOCKOUT → 400 with errorCode in CAMELCASE (verbatim — endpoint này
    /// khác convention error_code của các controller khác).
    /// </summary>
    private IActionResult MapUserGroupsFailure(UpdateUserGroupsResult result)
    {
        if (result.ErrorCode == "NOT_FOUND")
            return NotFound(new { status = "error", message = result.Message });

        return BadRequest(new { status = "error", message = result.Message, errorCode = result.ErrorCode });
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

        // [SEC-FIX S3, 2026-08-23] Company-scoping on CREATE (mirrors UpdateUser/DeleteUser scope
        // check in this controller + the Create conventions of Component/Consumable/SystemInfo):
        // a regular user may only create users for their own company (or a company-less floater);
        // Superuser (GetCurrentUserCompanyIdAsync → null) may create for any company. Never trust
        // the client-supplied CompanyId alone. Out-of-scope → 400 COMPANY_MISMATCH (this is a
        // create, not access to an existing record — no hide-existence).
        var actorCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (actorCompanyId.HasValue && command.CompanyId.HasValue && command.CompanyId.Value != actorCompanyId.Value)
            return BadRequest(new { status = "error", message = "Bạn chỉ được tạo người dùng cho công ty của mình.", error_code = "COMPANY_MISMATCH" });

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