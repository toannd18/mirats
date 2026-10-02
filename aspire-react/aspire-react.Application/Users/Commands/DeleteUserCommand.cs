using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace aspire_react.Server.Application.Users.Commands;

/// <summary>
/// [AUTH Phase 4] Soft-delete (deactivate) a user — LOCAL-ONLY (no Keycloak disable sync; D-3).
///
/// [FIX BUG-M 2026-10-02] The whole write is now ONE unit inside the handler — target lookup,
/// company-scope (hide-existence) and the deactivation lockout guard — and the ActionLog comes from
/// <see cref="ILoggableCommand{TResponse}"/> (ActionLogBehavior, committed in the SAME transaction as
/// the data change). Before this fix the controller did that work with a direct DbContext and then
/// wrote a SECOND ActionLog after the command had already committed (double log, non-atomic) —
/// see docs/BACKLOG.md BUG-M.
/// </summary>
public record DeleteUserCommand(
    Guid Id,
    Guid CurrentUserId,
    bool ActorIsRealmSuperUser = false) : IRequest<DeleteUserResult>, ILoggableCommand<DeleteUserResult>
{
    public ActionLogEntry? BuildLogEntry(DeleteUserResult response)
    {
        if (!response.Success) return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.User,
            ItemId = Id,
            ActionType = ActionType.Delete,
            CreatedBy = CurrentUserId,
            CompanyId = response.CompanyId,
            Note = response.Note,
            LogMeta = response.LogMeta
        };
    }
}

public record DeleteUserResult(
    bool Success,
    string Message,
    string? ErrorCode = null,
    Guid? CompanyId = null,
    string? Note = null,
    string? LogMeta = null);

public class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand, DeleteUserResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IPermissionLockoutGuard _lockoutGuard;
    private readonly ILogger<DeleteUserCommandHandler> _logger;

    public DeleteUserCommandHandler(
        IApplicationDbContext context,
        ICompanyScopeService companyScope,
        IPermissionLockoutGuard lockoutGuard,
        ILogger<DeleteUserCommandHandler> logger)
    {
        _context = context;
        _companyScope = companyScope;
        _lockoutGuard = lockoutGuard;
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

        // [Task J] Company-scoping: a regular user may only deactivate users of their own company
        // (or floater); Superuser (GetCurrentUserCompanyIdAsync → null) is unrestricted.
        // Out-of-scope behaves like not-found (hide-existence) — verbatim from the old controller.
        var actorCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (actorCompanyId.HasValue && user.CompanyId.HasValue && user.CompanyId.Value != actorCompanyId.Value)
        {
            return new DeleteUserResult(false, "User not found.", "USER_NOT_FOUND");
        }

        // [Task J] Anti self-lockout: deactivating the last holder of management capability
        // (superuser or admin) must be blocked — regardless of who performs it.
        if (await _lockoutGuard.WouldDeactivateUserLockoutAsync(
                request.CurrentUserId, request.Id, request.ActorIsRealmSuperUser))
        {
            return new DeleteUserResult(
                false,
                "Bạn không thể vô hiệu hóa người này khi họ là người cuối cùng còn giữ quyền quản trị.",
                "SELF_LOCKOUT");
        }

        user.IsActive = false;

        // [FIX BUG-M] ActionLog is persisted by ActionLogBehavior (ILoggableCommand) in the SAME
        // transaction as this SaveChanges — the old handler log + controller log (after commit) are
        // both gone: a write now produces exactly ONE audit entry.
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User '{Username}' (ID: {UserId}) deactivated in local DB (local-only — no Keycloak sync).",
            user.Username, user.Id);

        return new DeleteUserResult(
            true,
            "User deactivated successfully.",
            CompanyId: user.CompanyId,
            Note: $"Deactivated user: {user.Username} ({user.Email})",
            LogMeta: System.Text.Json.JsonSerializer.Serialize(new
            {
                username = user.Username,
                email = user.Email,
                isActive = false,
                companyId = user.CompanyId
            }));
    }
}
