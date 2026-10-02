using aspire_react.Server.Application.Users.DTOs;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Application.Common.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace aspire_react.Server.Application.Users.Commands;

/// <summary>
/// [AUTH Phase 4] Command to create a new user — LOCAL-ONLY (no Keycloak sync; D-3 approved).
/// Admin supplies the initial password (≥8) → PBKDF2 hash + MustChangePassword=true so the
/// user must change it at first login (no email system — offline handover, D-4).
///
/// [FIX BUG-M 2026-10-02] The controller-level glue (manual validator call + company-scope guard +
/// a SECOND ActionLog after the command committed) moved in here:
///   * company-scope check on CREATE (SEC-FIX S3 rule) is now the handler's first check;
///   * the ActionLog is produced by ActionLogBehavior (<see cref="ILoggableCommand{TResponse}"/>)
///     inside the SAME transaction as the insert → exactly ONE audit entry per create.
/// The FluentValidation validator still runs via the MediatR ValidationBehavior pipeline, and
/// ValidationExceptionHandler reproduces the exact `{status,message:"Validation failed.",errors}`
/// body the controller used to build by hand.
/// </summary>
public record CreateUserCommand : IRequest<CreateUserResult>, ILoggableCommand<CreateUserResult>
{
    public string Username { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? EmployeeNumber { get; init; }
    public string? JobTitle { get; init; }
    public string Password { get; init; } = string.Empty;
    public bool IsSuperUser { get; init; }
    public bool IsActive { get; init; } = true;
    public Guid? CompanyId { get; init; }
    public Guid? DepartmentId { get; init; }
    public Guid? LocationId { get; init; }
    /// <summary>Actor (local user id) — set by the controller from the `local_user_id` claim.</summary>
    public Guid CurrentUserId { get; init; }

    public ActionLogEntry? BuildLogEntry(CreateUserResult response)
    {
        // Soft-fail (scope/validation) returns before any row exists → nothing to log.
        if (!response.Success || response.User is null) return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.User,
            ItemId = response.User.Id,
            ActionType = ActionType.Create,
            CreatedBy = CurrentUserId,
            CompanyId = response.User.CompanyId,
            Note = $"Created user: {response.User.Username} ({response.User.Email})",
            LogMeta = System.Text.Json.JsonSerializer.Serialize(new
            {
                username = response.User.Username,
                email = response.User.Email,
                isActive = response.User.IsActive,
                isSuperUser = response.User.IsSuperUser,
                companyId = response.User.CompanyId
            })
        };
    }
}

public record CreateUserResult(
    bool Success,
    string Message,
    UserDto? User = null,
    string? ErrorCode = null);

public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, CreateUserResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasherService _passwordHasher;
    private readonly ICompanyScopeService _companyScope;
    private readonly ILogger<CreateUserCommandHandler> _logger;

    public CreateUserCommandHandler(
        IApplicationDbContext context,
        IPasswordHasherService passwordHasher,
        ICompanyScopeService companyScope,
        ILogger<CreateUserCommandHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _companyScope = companyScope;
        _logger = logger;
    }

    public async Task<CreateUserResult> Handle(
        CreateUserCommand request,
        CancellationToken cancellationToken)
    {
        // [AUTH Phase 4] LOCAL-ONLY creation (D-3): the old Keycloak sync block is removed —
        // the password is hashed here and the user MUST change it at first login (§4.4 policy).
        // Validator enforces ≥8 chars; defense-in-depth check here too.
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length < 8)
            return new CreateUserResult(false, "Mật khẩu ban đầu phải có ít nhất 8 ký tự.", ErrorCode: "VALIDATION_ERROR");

        // [SEC-FIX S3 / FIX BUG-M] Company-scoping on CREATE — moved verbatim from the controller:
        // a regular user may only create users for their OWN company (or a company-less floater);
        // superuser (scope null) may create for any company. Out-of-scope → 400 COMPANY_MISMATCH
        // (create, not access to an existing record → no hide-existence).
        var actorCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (actorCompanyId.HasValue && request.CompanyId.HasValue && request.CompanyId.Value != actorCompanyId.Value)
            return new CreateUserResult(false, "Bạn chỉ được tạo người dùng cho công ty của mình.", ErrorCode: "COMPANY_MISMATCH");

        // [FIX-N5 remainder] Department/Location references must exist AND be inside the actor's
        // scope — same rule as CompanyId: a regular admin may only attach users to
        // departments/locations of their own company (or floaters).
        var referenceCheck = await UserReferenceScope.ValidateAsync(
            _context, _companyScope, request.DepartmentId, request.LocationId, cancellationToken);
        if (referenceCheck.ErrorCode is not null)
            return new CreateUserResult(false, referenceCheck.Message!, ErrorCode: referenceCheck.ErrorCode);

        // === Save to local DB ===
        var user = new User
        {
            Username = request.Username.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            EmployeeNumber = request.EmployeeNumber?.Trim(),
            JobTitle = request.JobTitle?.Trim(),
            PasswordHash = _passwordHasher.Hash(request.Password),
            MustChangePassword = true,
            IsSuperUser = request.IsSuperUser,
            IsActive = request.IsActive,
            CompanyId = request.CompanyId,
            DepartmentId = request.DepartmentId,
            LocationId = request.LocationId,
        };

        _context.Users.Add(user);

        // [FIX BUG-M] Audit trail: ActionLogBehavior (ILoggableCommand) stages the log and commits it
        // together with this insert — the manual LogAction call that used to live here is gone.
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User '{Username}' saved to local DB with ID {UserId} (local password set, must change at first login).",
            user.Username, user.Id);

        var dto = MapToDto(user);
        return new CreateUserResult(true, "User created successfully.", User: dto);
    }

    private static UserDto MapToDto(User user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        Email = user.Email,
        FirstName = user.FirstName,
        LastName = user.LastName,
        EmployeeNumber = user.EmployeeNumber,
        JobTitle = user.JobTitle,
        IsSuperUser = user.IsSuperUser,
        HasPassword = true, // local creation always sets a password
        IsActive = user.IsActive,
        CompanyId = user.CompanyId,
        DepartmentId = user.DepartmentId,
        LocationId = user.LocationId,
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt,
    };
}
