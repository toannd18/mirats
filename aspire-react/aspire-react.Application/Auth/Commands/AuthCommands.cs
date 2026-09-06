using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Auth.Commands;

/// <summary>Shared result shape for login/refresh — controller sets the refresh cookie from
/// RefreshToken when Success (cookie is an HTTP concern — AUTH_MIGRATION_PLAYBOOK §11.2).</summary>
public record AuthTokenResult(
    bool Success,
    string? ErrorCode = null,
    string? AccessToken = null,
    string? RefreshToken = null,
    DateTime? RefreshExpiresAt = null,
    bool MustChangePassword = false);

/// <summary>
/// [AUTH Phase 1] POST /auth/login — password login. NOT ILoggableCommand: sign-in audit lives in
/// auth_login_attempts (richer: IP/success/timestamp) — deliberately not double-audited via
/// ActionLog (AUTH_MIGRATION_PLAYBOOK §11.2). Pipeline: IP burst check → user lookup (message is
/// GENERIC for both wrong-username and wrong-password — no user enumeration) → lockout window →
/// password verify (PBKDF2, RehashNeeded transparently upgrades the stored hash) → issue access
/// token (limited-scope when MustChangePassword) + refresh token row.
/// </summary>
public record LoginCommand(string Username, string Password, string IpAddress)
    : IRequest<AuthTokenResult>;

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Username).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public class LoginCommandHandler : IRequestHandler<LoginCommand, AuthTokenResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasherService _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IAuthAttemptService _attempts;

    public LoginCommandHandler(
        IApplicationDbContext context,
        IPasswordHasherService passwordHasher,
        ITokenService tokenService,
        IAuthAttemptService attempts)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _attempts = attempts;
    }

    public async Task<AuthTokenResult> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var usernameLower = (request.Username ?? string.Empty).Trim().ToLowerInvariant();
        var ipAddress = request.IpAddress ?? string.Empty;

        // Per-IP burst limit — cheaper check first (in-memory).
        if (await _attempts.IsIpBlockedAsync(ipAddress, cancellationToken))
            return new AuthTokenResult(false, "ACCOUNT_LOCKED");

        var genericFailure = async () =>
        {
            await _attempts.RecordAttemptAsync(usernameLower, ipAddress, false, cancellationToken);
            return new AuthTokenResult(false, "INVALID_CREDENTIALS");
        };

        var user = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username.ToLower() == usernameLower, cancellationToken);

        // GENERIC failure for: unknown username, no local password yet, wrong password.
        // The response never reveals which one it was (no user enumeration).
        if (user == null || !user.IsActive || string.IsNullOrEmpty(user.PasswordHash))
            return await genericFailure();

        // Escalating per-username lockout — checked BEFORE password verification.
        // Lockout = elapsed-since-LAST-FAILURE < LockoutSeconds(failure count): the 5th failure
        // locks for 60s, the 6th+ escalate (2^n, cap 15 min); when the countdown expires the user
        // can try again (their next failure re-locks with the escalated duration).
        var failures = await _attempts.GetConsecutiveFailuresAsync(usernameLower, cancellationToken);
        var lockoutSeconds = _attempts.LockoutSeconds(failures);
        if (lockoutSeconds > 0)
        {
            var lastFailureAt = await _attempts.GetLastFailureAtAsync(usernameLower, cancellationToken);
            var lockedUntil = (lastFailureAt ?? DateTime.UtcNow).AddSeconds(lockoutSeconds);
            if (DateTime.UtcNow < lockedUntil)
                return new AuthTokenResult(false, "ACCOUNT_LOCKED");
        }

        var verify = _passwordHasher.Verify(request.Password, user.PasswordHash);
        if (verify == PasswordVerifyResult.Failed)
            return await genericFailure();

        if (verify == PasswordVerifyResult.RehashNeeded)
        {
            // Transparent parameter upgrade: re-hash with current settings and persist.
            var tracked = await _context.Users.FirstAsync(u => u.Id == user.Id, cancellationToken);
            tracked.PasswordHash = _passwordHasher.Hash(request.Password);
            await _context.SaveChangesAsync(cancellationToken);
        }

        await _attempts.RecordAttemptAsync(usernameLower, ipAddress, true, cancellationToken);

        var mustChange = user.MustChangePassword;
        var accessToken = _tokenService.IssueAccessToken(user, mustChange);

        var refreshToken = _tokenService.GenerateRefreshToken();
        _context.UserCredentials.Add(new UserCredential
        {
            UserId = user.Id,
            TokenHash = _tokenService.HashToken(refreshToken),
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });
        await _context.SaveChangesAsync(cancellationToken);

        return new AuthTokenResult(true,
            AccessToken: accessToken,
            RefreshToken: refreshToken,
            RefreshExpiresAt: DateTime.UtcNow.AddDays(7),
            MustChangePassword: mustChange);
    }
}

/// <summary>
/// [AUTH Phase 1] POST /auth/refresh — rotate the refresh token (old row revoked + ReplacedById
/// chain) and issue a fresh access token. Reuse-detection: presenting an ALREADY-REVOKED token
/// revokes ALL of the user's sessions (stolen-token containment). NOT ILoggableCommand (§11.2).
/// </summary>
public record RefreshTokenCommand(string RawRefreshToken) : IRequest<AuthTokenResult>;

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, AuthTokenResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ITokenService _tokenService;

    public RefreshTokenCommandHandler(IApplicationDbContext context, ITokenService tokenService)
    {
        _context = context;
        _tokenService = tokenService;
    }

    public async Task<AuthTokenResult> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var tokenHash = _tokenService.HashToken(request.RawRefreshToken);
        var now = DateTime.UtcNow;

        var credential = await _context.UserCredentials
            .FirstOrDefaultAsync(c => c.TokenHash == tokenHash, cancellationToken);

        // Unknown token OR expired → plain rejection.
        if (credential == null || credential.ExpiresAt <= now)
            return new AuthTokenResult(false, "INVALID_CREDENTIALS");

        if (credential.RevokedAt.HasValue)
        {
            // REUSE DETECTION: this token was already rotated — treat every session of the user
            // as compromised and revoke them all (standard refresh-token containment).
            var all = await _context.UserCredentials
                .Where(c => c.UserId == credential.UserId && c.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var row in all) row.RevokedAt = now;
            await _context.SaveChangesAsync(cancellationToken);
            return new AuthTokenResult(false, "INVALID_CREDENTIALS");
        }

        var user = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == credential.UserId && u.IsActive, cancellationToken);
        if (user == null)
            return new AuthTokenResult(false, "INVALID_CREDENTIALS");

        // Rotation: revoke the presented row, link its replacement, issue a new pair.
        credential.RevokedAt = now;
        var newRefresh = _tokenService.GenerateRefreshToken();
        var newCredential = new UserCredential
        {
            UserId = user.Id,
            TokenHash = _tokenService.HashToken(newRefresh),
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        _context.UserCredentials.Add(newCredential);
        await _context.SaveChangesAsync(cancellationToken);
        credential.ReplacedById = newCredential.Id;
        await _context.SaveChangesAsync(cancellationToken);

        var mustChange = user.MustChangePassword;
        return new AuthTokenResult(true,
            AccessToken: _tokenService.IssueAccessToken(user, mustChange),
            RefreshToken: newRefresh,
            RefreshExpiresAt: newCredential.ExpiresAt,
            MustChangePassword: mustChange);
    }
}

/// <summary>
/// [AUTH Phase 1] POST /auth/logout — revoke the presented refresh token. NOT ILoggableCommand.
/// </summary>
public record LogoutCommand(string RawRefreshToken) : IRequest<AuthSimpleResult>;

public record AuthSimpleResult(bool Success, string? ErrorCode = null);

public class LogoutCommandHandler : IRequestHandler<LogoutCommand, AuthSimpleResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ITokenService _tokenService;

    public LogoutCommandHandler(IApplicationDbContext context, ITokenService tokenService)
    {
        _context = context;
        _tokenService = tokenService;
    }

    public async Task<AuthSimpleResult> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var tokenHash = _tokenService.HashToken(request.RawRefreshToken);
        var credential = await _context.UserCredentials
            .FirstOrDefaultAsync(c => c.TokenHash == tokenHash && c.RevokedAt == null, cancellationToken);
        if (credential != null)
        {
            credential.RevokedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
        // Idempotent: logging out with an unknown/already-revoked token still succeeds.
        return new AuthSimpleResult(true);
    }
}
