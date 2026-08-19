using aspire_react.Server.Application.Users.DTOs;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace aspire_react.Server.Application.Users.Commands;

/// <summary>
/// Command to create a new user. Syncs one-way to Keycloak before saving to local DB.
/// </summary>
public record CreateUserCommand : IRequest<CreateUserResult>
{
    public string Username { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? EmployeeNumber { get; init; }
    public string? JobTitle { get; init; }
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
    private readonly AppDbContext _context;
    private readonly IKeycloakService _keycloakService;
    private readonly IActionLogService _actionLogService;
    private readonly ILogger<CreateUserCommandHandler> _logger;

    public CreateUserCommandHandler(
        AppDbContext context,
        IKeycloakService keycloakService,
        IActionLogService actionLogService,
        ILogger<CreateUserCommandHandler> logger)
    {
        _context = context;
        _keycloakService = keycloakService;
        _actionLogService = actionLogService;
        _logger = logger;
    }

    public async Task<CreateUserResult> Handle(
        CreateUserCommand request,
        CancellationToken cancellationToken)
    {
        // === Step 1: Create user in Keycloak first ===
        try
        {
            await _keycloakService.CreateUserAsync(
                request.Username.Trim(),
                request.Email.Trim().ToLowerInvariant(),
                request.FirstName.Trim(),
                request.LastName.Trim(),
                request.IsActive,
                cancellationToken);

            _logger.LogInformation("User '{Username}' created in Keycloak successfully.",
                request.Username);
        }
        catch (KeycloakApiException kex)
        {
            _logger.LogWarning(kex, "Failed to create user '{Username}' in Keycloak.", request.Username);
            return new CreateUserResult(
                false,
                kex.Message,
                ErrorCode: kex.ErrorCode ?? "KEYCLOAK_ERROR");
        }
        catch (ArgumentException aex)
        {
            return new CreateUserResult(false, aex.Message, ErrorCode: "VALIDATION_ERROR");
        }

        // === Step 2: Save to local DB ===
        var user = new User
        {
            Username = request.Username.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            EmployeeNumber = request.EmployeeNumber?.Trim(),
            JobTitle = request.JobTitle?.Trim(),
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

        _logger.LogInformation("User '{Username}' saved to local DB with ID {UserId}.",
            user.Username, user.Id);

        // === Step 3: Add to superuser group in Keycloak if applicable ===
        if (request.IsSuperUser)
        {
            try
            {
                await _keycloakService.AddUserToSuperUserGroupAsync(
                    request.Username.Trim(),
                    cancellationToken);
                _logger.LogInformation("User '{Username}' added to superuser group in Keycloak.",
                    request.Username);
            }
            catch (KeycloakApiException kex)
            {
                _logger.LogWarning(kex,
                    "User '{Username}' created in Keycloak but failed to add to superuser group. " +
                    "Manual intervention may be required.", request.Username);
                // Do not fail the whole operation — the user was created successfully
            }
        }

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
        IsActive = user.IsActive,
        CompanyId = user.CompanyId,
        DepartmentId = user.DepartmentId,
        LocationId = user.LocationId,
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt,
    };
}