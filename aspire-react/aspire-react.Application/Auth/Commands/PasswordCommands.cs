using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Auth.Commands;

/// <summary>
/// [AUTH Phase 1] POST /auth/password — the logged-in user changes their OWN password (current
/// password re-verified; new ≥ 8 chars per the approved policy). Side effects: MUST-change flag
/// cleared, all OTHER refresh sessions revoked (this device stays logged in), ILoggableCommand
/// ActionLog (Update — never contains the password or its hash).
///
/// [FIX-N14 2026-10-02] The "this device stays logged in" promise was DOCUMENTED but not
/// implemented: the handler revoked EVERY non-revoked credential of the user, the caller's own
/// refresh cookie included (so the device kept working only until its access token expired, then
/// was silently signed out). <see cref="CurrentRawRefreshToken"/> now carries the caller's cookie so
/// that credential is excluded from the revoke — behaviour matches the documentation, and stolen
/// tokens from other devices are still killed.
/// </summary>
public record ChangePasswordCommand(
    Guid CurrentUserId,
    string CurrentPassword,
    string NewPassword,
    string? CurrentRawRefreshToken = null)
    : IRequest<AuthSimpleResult>, ILoggableCommand<AuthSimpleResult>
{
    public ActionLogEntry? BuildLogEntry(AuthSimpleResult response)
    {
        if (!response.Success) return null;
        return new ActionLogEntry
        {
            ItemType = ItemType.User,
            ItemId = CurrentUserId,
            ActionType = ActionType.Update,
            CreatedBy = CurrentUserId,
            CompanyId = null, // resolved in-handler would need a query; User has optional company — log without company (same as Groups)
            Note = "Đổi mật khẩu tài khoản"
        };
    }
}

public class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(8).WithMessage("Mật khẩu mới phải có ít nhất 8 ký tự.");
    }
}

public class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, AuthSimpleResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasherService _passwordHasher;
    private readonly ITokenService _tokenService;

    public ChangePasswordCommandHandler(IApplicationDbContext context, IPasswordHasherService passwordHasher, ITokenService tokenService)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    public async Task<AuthSimpleResult> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == request.CurrentUserId, cancellationToken);
        if (user == null || !user.IsActive)
            return new AuthSimpleResult(false, "USER_NOT_FOUND");

        // Current password must match (self-service change — no brute-force gate needed here:
        // the caller is already authenticated; the login endpoint is the brute-force surface).
        if (string.IsNullOrEmpty(user.PasswordHash)
            || _passwordHasher.Verify(request.CurrentPassword, user.PasswordHash) == PasswordVerifyResult.Failed)
            return new AuthSimpleResult(false, "INVALID_CREDENTIALS");

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        user.MustChangePassword = false;

        // Revoke every OTHER session — stolen refresh tokens from before the change stop working.
        // [FIX-N14] The caller's own refresh cookie is EXCLUDED (hash-compared, never the raw token),
        // so the device that performed the change stays logged in as documented.
        var currentTokenHash = string.IsNullOrWhiteSpace(request.CurrentRawRefreshToken)
            ? null
            : _tokenService.HashToken(request.CurrentRawRefreshToken);
        var others = await _context.UserCredentials
            .Where(c => c.UserId == user.Id && c.RevokedAt == null
                        && (currentTokenHash == null || c.TokenHash != currentTokenHash))
            .ToListAsync(cancellationToken);
        foreach (var row in others) row.RevokedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return new AuthSimpleResult(true);
    }
}

/// <summary>
/// [AUTH Phase 1] POST /users/{id}/reset-password — ADMIN resets a user's password (the approved
/// migration path: no email flow). Sets MustChangePassword so the user must change it at next
/// login before using the system, and revokes all their existing sessions. ILoggableCommand logs
/// WHO reset WHOSE password — never the password itself.
/// </summary>
public record AdminResetPasswordCommand(
    Guid TargetUserId,
    string NewPassword,
    Guid CurrentUserId)
    : IRequest<AuthSimpleResult>, ILoggableCommand<AuthSimpleResult>
{
    public ActionLogEntry? BuildLogEntry(AuthSimpleResult response)
    {
        if (!response.Success) return null;
        return new ActionLogEntry
        {
            ItemType = ItemType.User,
            ItemId = TargetUserId,
            ActionType = ActionType.Update,
            CreatedBy = CurrentUserId,
            CompanyId = null,
            Note = "Admin đặt lại mật khẩu người dùng"
        };
    }
}

public class AdminResetPasswordCommandValidator : AbstractValidator<AdminResetPasswordCommand>
{
    public AdminResetPasswordCommandValidator()
    {
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(8).WithMessage("Mật khẩu mới phải có ít nhất 8 ký tự.");
    }
}

public class AdminResetPasswordCommandHandler : IRequestHandler<AdminResetPasswordCommand, AuthSimpleResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasherService _passwordHasher;
    private readonly ICompanyScopeService _companyScope;

    public AdminResetPasswordCommandHandler(
        IApplicationDbContext context,
        IPasswordHasherService passwordHasher,
        ICompanyScopeService companyScope)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _companyScope = companyScope;
    }

    public async Task<AuthSimpleResult> Handle(AdminResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == request.TargetUserId, cancellationToken);
        if (user == null)
            return new AuthSimpleResult(false, "USER_NOT_FOUND");

        // [FIX BUG-M 2026-10-02] Company-scoping moved out of UsersController.ResetPassword so the
        // whole write is one unit (the controller is now IMediator-only): a regular admin may only
        // reset users of their OWN company; out-of-scope behaves like not-found (hide-existence);
        // superuser (scope null) is unrestricted. The controller maps every failure to 404.
        var actorCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (actorCompanyId.HasValue && user.CompanyId.HasValue && user.CompanyId.Value != actorCompanyId.Value)
            return new AuthSimpleResult(false, "USER_NOT_FOUND");

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        user.MustChangePassword = true;

        // Revoke ALL sessions — the reset invalidates every existing login of the target user.
        var sessions = await _context.UserCredentials
            .Where(c => c.UserId == user.Id && c.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var row in sessions) row.RevokedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return new AuthSimpleResult(true);
    }
}
