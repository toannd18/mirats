using aspire_react.Server.Application.Users.DTOs;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace aspire_react.Server.Application.Users.Commands;

/// <summary>
/// [AUTH Phase 4] Command to update an existing user — LOCAL-ONLY (no Keycloak sync; D-3).
/// IsSuperUser is a purely local flag now.
/// </summary>
/// <remarks>
/// [FIX BUG-M 2026-10-02] The controller-level glue moved in here so the write is ONE unit and
/// produces exactly ONE ActionLog: target lookup + company-scope (hide-existence) + demote-lockout
/// guard (needs <c>ActorIsRealmSuperUser</c>, resolved from claims by the controller) now run inside
/// the handler, and the log comes from <see cref="ILoggableCommand{TResponse}"/> (ActionLogBehavior,
/// same transaction as the data change). Previously the handler logged once and the controller
/// logged a SECOND time after the command had already committed.
/// </remarks>
/// <remarks>
/// [FIX-N1 2026-10-02] Patch-safety (Task M1/M2 convention): a field that is ABSENT from the
/// payload is never written — the stored value is preserved (before this fix CompanyId /
/// DepartmentId / LocationId / EmployeeNumber / JobTitle were assigned unconditionally, so a
/// partial payload silently wiped them — the exact "wiped real data" bug class of BUG-E/N).
/// Three-valued convention for the nullable Guid fields (CompanyId, DepartmentId, LocationId),
/// reusing the project-wide "Guid.Empty = floater/none" sentinel already established in
/// CompanyScopeService (Guid.Empty for a company-less regular user), AssetMaintenance.CompanyId
/// (Guid.Empty = floater) and ImportCommands (Guid.Empty → COMPANY_REQUIRED):
///   absent (JSON null)  → KEEP the stored value
///   Guid.Empty          → CLEAR to null (CompanyId → company-less floater)
///   a real Guid         → SET it (CompanyId is additionally scope-checked, see FIX-N5)
/// For the two string fields (EmployeeNumber, JobTitle) the convention is the usual patch one:
/// absent → KEEP, sent empty string → cleared to "" (same semantics BUG-N documented for Notes).
/// [FIX-N5 2026-10-02] Company-scoping for the NEW CompanyId (Task L2 / CreateUser pattern):
/// a regular user may only assign a company equal to their own scope or clear to floater;
/// a superuser (scope null) is unrestricted. Validated BEFORE any mutation.
/// </remarks>
public record UpdateUserCommand : IRequest<UpdateUserResult>, ILoggableCommand<UpdateUserResult>
{
    public Guid Id { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? EmployeeNumber { get; init; }
    public string? JobTitle { get; init; }
    // Task M2: nullable so a partial payload that omits these does NOT silently reset them to
    // false (which would strip admin rights / deactivate the account).
    public bool? IsSuperUser { get; init; }
    public bool? IsActive { get; init; }
    public Guid? CompanyId { get; init; }
    public Guid? DepartmentId { get; init; }
    public Guid? LocationId { get; init; }
    /// <summary>Actor (local user id) — set by the controller from the `local_user_id` claim.</summary>
    public Guid CurrentUserId { get; init; }
    /// <summary>Realm/claim-level superuser flag — resolved by the controller (handlers cannot read HttpContext).</summary>
    public bool ActorIsRealmSuperUser { get; init; }

    public ActionLogEntry? BuildLogEntry(UpdateUserResult response)
    {
        if (!response.Success) return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.User,
            ItemId = Id,
            ActionType = ActionType.Update,
            CreatedBy = CurrentUserId,
            CompanyId = response.CompanyId,
            Note = response.Note,
            LogMeta = response.LogMeta
        };
    }
}

public record UpdateUserResult(
    bool Success,
    string Message,
    UserDto? User = null,
    string? ErrorCode = null,
    Guid? CompanyId = null,
    string? Note = null,
    string? LogMeta = null);

public class UpdateUserCommandHandler : IRequestHandler<UpdateUserCommand, UpdateUserResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IPermissionLockoutGuard _lockoutGuard;
    private readonly ILogger<UpdateUserCommandHandler> _logger;

    public UpdateUserCommandHandler(
        IApplicationDbContext context,
        ICompanyScopeService companyScope,
        IPermissionLockoutGuard lockoutGuard,
        ILogger<UpdateUserCommandHandler> logger)
    {
        _context = context;
        _companyScope = companyScope;
        _lockoutGuard = lockoutGuard;
        _logger = logger;
    }

    public async Task<UpdateUserResult> Handle(
        UpdateUserCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .Include(u => u.Company)
            .Include(u => u.Department)
            .Include(u => u.Location)
            .FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken);

        if (user == null)
        {
            return new UpdateUserResult(false, "User not found.", ErrorCode: "USER_NOT_FOUND");
        }

        // [Task J / FIX BUG-M] Company-scoping on the TARGET row (moved from the controller):
        // a regular user may only update users of their own company (or floater); superuser
        // (GetCurrentUserCompanyIdAsync → null) is unrestricted. Out-of-scope = hide-existence 404.
        var actorCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (actorCompanyId.HasValue && user.CompanyId.HasValue && user.CompanyId.Value != actorCompanyId.Value)
        {
            return new UpdateUserResult(false, "User not found.", ErrorCode: "USER_NOT_FOUND");
        }

        // [Task J / FIX BUG-M] Anti self-lockout (moved from the controller): demoting the last
        // superuser must be blocked — regardless of who performs it.
        if (request.IsSuperUser == false && user.IsSuperUser)
        {
            if (await _lockoutGuard.WouldDemoteSuperUserLockoutAsync(
                    request.CurrentUserId, request.Id, request.ActorIsRealmSuperUser))
            {
                return new UpdateUserResult(
                    false,
                    "Bạn không thể hạ quyền superuser khi người này là superuser cuối cùng còn giữ quyền quản trị.",
                    ErrorCode: "SELF_LOCKOUT");
            }
        }

        // [FIX-N5] Company-scoping for the NEW CompanyId — checked BEFORE any mutation (Task L2 /
        // CreateUser pattern). A regular user may only assign their own company; the Guid.Empty
        // sentinel (clear → floater) is always allowed, mirroring CreateUser's "own company or
        // floater" rule; for a superuser GetCurrentUserCompanyIdAsync() returns null → unrestricted.
        if (request.CompanyId.HasValue && request.CompanyId.Value != Guid.Empty
            && actorCompanyId.HasValue
            && request.CompanyId.Value != actorCompanyId.Value)
        {
            return new UpdateUserResult(
                false,
                "Bạn chỉ được gán người dùng cho công ty của mình.",
                ErrorCode: "COMPANY_MISMATCH");
        }

        // [FIX-N5 remainder] Department/Location references must exist AND be inside the actor's
        // scope (a company-A admin could previously attach a user to company-B's department or
        // location). Checked BEFORE any mutation, only for the fields actually sent.
        var referenceCheck = await UserReferenceScope.ValidateAsync(
            _context, _companyScope, request.DepartmentId, request.LocationId, cancellationToken);
        if (referenceCheck.ErrorCode is not null)
        {
            return new UpdateUserResult(false, referenceCheck.Message!, ErrorCode: referenceCheck.ErrorCode);
        }

        var previousIsSuperUser = user.IsSuperUser;
        var previousEmail = user.Email;
        var previousIsActive = user.IsActive;
        var previousCompanyId = user.CompanyId;
        var previousDepartmentId = user.DepartmentId;
        var previousLocationId = user.LocationId;

        // Update local entity properties
        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.Email = request.Email.Trim().ToLowerInvariant();
        // [FIX-N1] Patch semantics for the two free-text fields: ABSENT → keep the stored value
        // (before the fix they were assigned unconditionally → a partial payload wiped them).
        if (request.EmployeeNumber is not null) user.EmployeeNumber = request.EmployeeNumber.Trim();
        if (request.JobTitle is not null) user.JobTitle = request.JobTitle.Trim();
        // Task M2 patch semantics: only apply flags that were explicitly sent (absent → keep current).
        if (request.IsSuperUser.HasValue) user.IsSuperUser = request.IsSuperUser.Value;
        if (request.IsActive.HasValue) user.IsActive = request.IsActive.Value;
        // [FIX-N1] Patch semantics for the nullable Guid fields: ABSENT → keep; Guid.Empty sentinel
        // → clear to null (CompanyId becomes a company-less floater); real Guid → set.
        if (request.CompanyId.HasValue) user.CompanyId = NullIfSentinel(request.CompanyId);
        if (request.DepartmentId.HasValue) user.DepartmentId = NullIfSentinel(request.DepartmentId);
        if (request.LocationId.HasValue) user.LocationId = NullIfSentinel(request.LocationId);

        // [AUTH Phase 4] LOCAL-ONLY update (D-3): the Keycloak sync block (UpdateUserAsync +
        // superuser group add/remove) is removed — IsSuperUser is a purely local flag now.

        // [FIX BUG-M] ActionLog is persisted by ActionLogBehavior (ILoggableCommand) in the SAME
        // transaction as this SaveChanges — the manual log here + the controller log (after commit)
        // are both gone: a write now produces exactly ONE audit entry.
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User '{Username}' (ID: {UserId}) updated in local DB.",
            user.Username, user.Id);

        var dto = new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            EmployeeNumber = user.EmployeeNumber,
            JobTitle = user.JobTitle,
            IsSuperUser = user.IsSuperUser,
            HasPassword = user.PasswordHash != null,
            IsActive = user.IsActive,
            CompanyId = user.CompanyId,
            CompanyName = user.Company?.Name,
            DepartmentId = user.DepartmentId,
            DepartmentName = user.Department?.Name,
            LocationId = user.LocationId,
            LocationName = user.Location?.Name,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
        };

        return new UpdateUserResult(
            true,
            "User updated successfully.",
            User: dto,
            CompanyId: user.CompanyId,
            Note: $"Updated user: {user.Username} ({user.Email})",
            LogMeta: System.Text.Json.JsonSerializer.Serialize(new
            {
                changes = new Dictionary<string, object?>
                {
                    ["email"] = new { old = previousEmail, @new = user.Email },
                    ["isActive"] = new { old = previousIsActive, @new = user.IsActive },
                    ["isSuperUser"] = new { old = previousIsSuperUser, @new = user.IsSuperUser },
                    ["companyId"] = new { old = previousCompanyId, @new = user.CompanyId },
                    ["departmentId"] = new { old = previousDepartmentId, @new = user.DepartmentId },
                    ["locationId"] = new { old = previousLocationId, @new = user.LocationId }
                }
            }));
    }

    /// <summary>
    /// [FIX-N1] Applies the project-wide "Guid.Empty = floater/none" sentinel: Guid.Empty → null
    /// (clear), any other value → itself. Callers invoke this only when the field was sent.
    /// </summary>
    private static Guid? NullIfSentinel(Guid? value)
        => value.HasValue && value.Value != Guid.Empty ? value.Value : null;
}
