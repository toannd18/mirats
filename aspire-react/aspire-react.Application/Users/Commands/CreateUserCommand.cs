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
/// </summary>
public record CreateUserCommand : IRequest<CreateUserResult>
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
    private readonly IActionLogService _actionLogService;
    private readonly ICompanyScopeService _companyScope;
    private readonly ILogger<CreateUserCommandHandler> _logger;

    public CreateUserCommandHandler(
        IApplicationDbContext context,
        IPasswordHasherService passwordHasher,
        IActionLogService actionLogService,
        ICompanyScopeService companyScope,
        ILogger<CreateUserCommandHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _actionLogService = actionLogService;
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

        // [FIX-N5 remainder] Department/Location references must exist AND be inside the actor's
        // scope — same rule as CompanyId (checked in the controller for this command): a regular
        // admin may only attach users to departments/locations of their own company (or floaters).
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

        // Audit trail (ST5/F10): record actor + affected user; persisted atomically with the new user.
        var actorId = await _actionLogService.GetCurrentUserIdAsync();
        _actionLogService.LogAction(
            itemType: ItemType.User,
            itemId: user.Id,
            actionType: ActionType.Create,
            loggedByUserId: actorId,
            companyId: user.CompanyId,
            note: $"Created user: {user.Username} ({user.Email})",
            logMeta: System.Text.Json.JsonSerializer.Serialize(new
            {
                username = user.Username,
                email = user.Email,
                isActive = user.IsActive,
                isSuperUser = user.IsSuperUser,
                companyId = user.CompanyId
            }));

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