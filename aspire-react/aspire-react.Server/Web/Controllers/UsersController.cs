using System.Security.Claims;
using aspire_react.Server.Application.Auth.Commands;
using aspire_react.Server.Application.Users.Commands;
using aspire_react.Server.Application.Users.Queries;
using aspire_react.Server.Infrastructure.Services;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

[ApiController]
[Route("api/v1/users")]
/// <summary>
/// [FIX BUG-M 2026-10-02] THIN 100%: the constructor only takes <see cref="IMediator"/>.
///
/// History: the [Giai đoạn 3] migration deliberately left 3 write actions (Create/Update/Delete)
/// hybrid (IMediator + AppDbContext + ActionLog/lockout/scope services) because they were tied to
/// the Keycloak user sync. That reason disappeared in AUTH Phase 5 (Keycloak removed) and the
/// hybrid glue caused BUG-M: every write produced TWO ActionLog rows (one in the command handler,
/// one here AFTER the command had already committed — the second not atomic with the data).
///
/// All of that logic now lives in the commands:
///   * CreateUserCommand  — company-scope guard (SEC-FIX S3) + validation via the MediatR pipeline;
///   * UpdateUserCommand  — target company-scope, demote-lockout guard, new-CompanyId scope;
///   * DeleteUserCommand  — target company-scope + deactivate-lockout guard;
///   * AdminResetPasswordCommand — target company-scope.
/// Each write implements ILoggableCommand ⇒ exactly ONE ActionLog, committed with the data.
/// The controller keeps only claim parsing (local_user_id / realm-superuser) and HTTP mapping.
/// Error-shape parity kept: USER_NOT_FOUND → 404; SELF_LOCKOUT → 400 camelCase `errorCode`;
/// COMPANY_MISMATCH / RESOURCE_NOT_FOUND → 400 snake_case `error_code`; other → 400 camelCase.
/// </summary>
public class UsersController : ControllerBase
{
    private readonly IMediator _mediator;

    public UsersController(IMediator mediator)
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
    private bool IsRealmSuperUser() => User != null && SuperuserClaims.IsSuperUser(User);

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
    /// Creates a new user — LOCAL-ONLY (AUTH Phase 4): password ban đầu + MustChangePassword.
    /// [FIX BUG-M] Thin: the FluentValidation validator runs in the MediatR pipeline (same 400 body
    /// via ValidationExceptionHandler), and the company-scope guard + ActionLog live in the command.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "users.create")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserCommand command)
    {
        var result = await _mediator.Send(command with { CurrentUserId = GetCurrentUserId() });

        if (!result.Success)
        {
            // [AUTH Phase 4] local-only creation — KEYCLOAK_* outcomes no longer occur.
            return result.ErrorCode switch
            {
                "VALIDATION_ERROR" => BadRequest(new { status = "error", message = result.Message }),
                // [FIX-N5 remainder] Scoping/reference failures use the same snake_case body as the
                // controller-level COMPANY_MISMATCH guard above (:246-248).
                "COMPANY_MISMATCH" or "RESOURCE_NOT_FOUND"
                    => BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode }),
                _ => BadRequest(new { status = "error", message = result.Message, errorCode = result.ErrorCode })
            };
        }

        // [FIX BUG-M] No ActionLog here any more: CreateUserCommand is an ILoggableCommand, so
        // ActionLogBehavior commits the single audit entry together with the insert.
        return CreatedAtAction(nameof(GetUser), new { id = result.User!.Id }, new
        {
            status = "success",
            message = result.Message,
            data = result.User
        });
    }

    /// <summary>
    /// Updates an existing user — LOCAL-ONLY (AUTH Phase 4).
    /// [FIX BUG-M] Thin: validation runs in the MediatR pipeline; target company-scope, the
    /// demote-lockout guard and the ActionLog all live in UpdateUserCommand.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = "admin")]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserCommand command)
    {
        if (id != command.Id)
            return BadRequest(new { status = "error", message = "ID mismatch." });

        var result = await _mediator.Send(command with
        {
            CurrentUserId = GetCurrentUserId(),
            ActorIsRealmSuperUser = IsRealmSuperUser()
        });

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                "USER_NOT_FOUND" => NotFound(new { status = "error", message = result.Message }),
                // [FIX BUG-M] Lockout guard moved into the handler → same 400 camelCase body as before.
                "SELF_LOCKOUT" => BadRequest(new { status = "error", message = result.Message, errorCode = result.ErrorCode }),
                // [FIX-N5] Company-scoping failure uses the snake_case `error_code` body exactly like
                // CreateUser's COMPANY_MISMATCH guard and ERROR_CODES §1.3; the rest of this
                // controller's failures keep the verbatim camelCase `errorCode` quirk.
                "COMPANY_MISMATCH" or "RESOURCE_NOT_FOUND"
                    => BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode }),
                _ => BadRequest(new { status = "error", message = result.Message, errorCode = result.ErrorCode })
            };
        }

        // [FIX BUG-M] No ActionLog here any more — UpdateUserCommand is an ILoggableCommand, so the
        // single audit entry is committed by ActionLogBehavior with the data change.
        return Ok(new
        {
            status = "success",
            message = result.Message,
            data = result.User
        });
    }

    /// <summary>
    /// [AUTH Phase 1] Admin resets a user's password (the approved Keycloak-migration path — no
    /// email flow). Forces MustChangePassword at next login and revokes the user's sessions.
    /// Thin MediatR mapping over AdminResetPasswordCommand (ILoggableCommand logs who reset whom;
    /// [FIX BUG-M] the target company-scope check moved into that handler).
    /// </summary>
    [HttpPost("{id:guid}/reset-password")]
    [Authorize(Policy = "users.edit")]
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordRequest request)
    {
        var result = await _mediator.Send(new AdminResetPasswordCommand(id, request.NewPassword, GetCurrentUserId()));

        if (!result.Success)
            return NotFound(new { status = "error", message = "User not found." });

        return Ok(new { status = "success", message = "Đã đặt lại mật khẩu. Người dùng sẽ phải đổi mật khẩu ở lần đăng nhập kế tiếp." });
    }

    public record ResetPasswordRequest(string NewPassword);

    /// <summary>
    /// Deactivates a user (soft delete) — LOCAL-ONLY (AUTH Phase 4).
    /// [FIX BUG-M] Thin: target company-scope + deactivate-lockout guard + ActionLog live in
    /// DeleteUserCommand (ILoggableCommand), so the write logs exactly once, atomically.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "users.delete")]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        var result = await _mediator.Send(new DeleteUserCommand(id, GetCurrentUserId(), IsRealmSuperUser()));

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                "USER_NOT_FOUND" => NotFound(new { status = "error", message = result.Message }),
                // Lockout guard moved into the handler → keep the old 400 camelCase body.
                "SELF_LOCKOUT" => BadRequest(new { status = "error", message = result.Message, errorCode = result.ErrorCode }),
                _ => BadRequest(new { status = "error", message = result.Message })
            };
        }

        // [FIX BUG-M] No ActionLog here any more (was the non-atomic second log of BUG-M).
        return Ok(new { status = "success", message = result.Message });
    }
}

public record UpdateUserGroupsRequest(List<Guid> GroupIds);