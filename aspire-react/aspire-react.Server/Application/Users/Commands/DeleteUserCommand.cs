using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace aspire_react.Server.Application.Users.Commands;

/// <summary>
/// Command to soft-delete (deactivate) a user.
/// Syncs disable to Keycloak.
/// </summary>
public record DeleteUserCommand(Guid Id) : IRequest<DeleteUserResult>;

public record DeleteUserResult(bool Success, string Message, string? ErrorCode = null);

public class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand, DeleteUserResult>
{
    private readonly AppDbContext _context;
    private readonly IKeycloakService _keycloakService;
    private readonly IActionLogService _actionLogService;
    private readonly ILogger<DeleteUserCommandHandler> _logger;

    public DeleteUserCommandHandler(
        AppDbContext context,
        IKeycloakService keycloakService,
        IActionLogService actionLogService,
        ILogger<DeleteUserCommandHandler> logger)
    {
        _context = context;
        _keycloakService = keycloakService;
        _actionLogService = actionLogService;
        _logger = logger;
    }

    public async Task<DeleteUserResult> Handle(
        DeleteUserCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken);

        if (user == null)
        {
            return new DeleteUserResult(false, "User not found.", "USER_NOT_FOUND");
        }

        user.IsActive = false;

        // Audit trail (ST5/F10): record the actor + deactivated user; persisted with the deactivation.
        var actorId = await _actionLogService.GetCurrentUserIdAsync();
        _actionLogService.LogAction(
            itemType: ItemType.User,
            itemId: user.Id,
            actionType: ActionType.Delete,
            loggedByUserId: actorId,
            companyId: user.CompanyId,
            note: $"Deactivated user: {user.Username} ({user.Email})",
            logMeta: System.Text.Json.JsonSerializer.Serialize(new
            {
                username = user.Username,
                email = user.Email,
                isActive = false,
                companyId = user.CompanyId
            }));

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User '{Username}' (ID: {UserId}) deactivated in local DB.",
            user.Username, user.Id);

        // Sync disable to Keycloak — fire and forget, don't fail on Keycloak errors
        try
        {
            await _keycloakService.DisableUserAsync(user.Username, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to disable user '{Username}' in Keycloak. Local DB updated successfully.",
                user.Username);
        }

        return new DeleteUserResult(true, "User deactivated successfully.");
    }
}