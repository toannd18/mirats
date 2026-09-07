using aspire_react.Server.Application.Users.DTOs;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Exceptions;
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
public record UpdateUserCommand : IRequest<UpdateUserResult>
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
}

public record UpdateUserResult(
    bool Success,
    string Message,
    UserDto? User = null,
    string? ErrorCode = null);

public class UpdateUserCommandHandler : IRequestHandler<UpdateUserCommand, UpdateUserResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IActionLogService _actionLogService;
    private readonly ILogger<UpdateUserCommandHandler> _logger;

    public UpdateUserCommandHandler(
        IApplicationDbContext context,
        IActionLogService actionLogService,
        ILogger<UpdateUserCommandHandler> logger)
    {
        _context = context;
        _actionLogService = actionLogService;
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
        user.EmployeeNumber = request.EmployeeNumber?.Trim();
        user.JobTitle = request.JobTitle?.Trim();
        // Task M2 patch semantics: only apply flags that were explicitly sent (absent → keep current).
        if (request.IsSuperUser.HasValue) user.IsSuperUser = request.IsSuperUser.Value;
        if (request.IsActive.HasValue) user.IsActive = request.IsActive.Value;
        user.CompanyId = request.CompanyId;
        user.DepartmentId = request.DepartmentId;
        user.LocationId = request.LocationId;

        // [AUTH Phase 4] LOCAL-ONLY update (D-3): the Keycloak sync block (UpdateUserAsync +
        // superuser group add/remove) is removed — IsSuperUser is a purely local flag now.

        // === Save to local DB ===
        // Audit trail (ST5/F10): record actor + affected user + meaningful changes; persisted with the update.
        var actorId = await _actionLogService.GetCurrentUserIdAsync();
        _actionLogService.LogAction(
            itemType: ItemType.User,
            itemId: user.Id,
            actionType: ActionType.Update,
            loggedByUserId: actorId,
            companyId: user.CompanyId,
            note: $"Updated user: {user.Username} ({user.Email})",
            logMeta: System.Text.Json.JsonSerializer.Serialize(new
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

        return new UpdateUserResult(true, "User updated successfully.", User: dto);
    }
}