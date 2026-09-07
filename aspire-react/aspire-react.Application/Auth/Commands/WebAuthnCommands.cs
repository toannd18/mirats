using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Auth.Commands;

/// <summary>
/// [AUTH Phase 3] Shared token-session issuing for BOTH password login and passkey login —
/// identical contract (15-min access JWT, 7-day refresh row, limited 10-min token when the user
/// must change password). Extracted so the two login paths can never drift.
/// </summary>
internal static class AuthSessionIssuer
{
    public static async Task<AuthTokenResult> IssueAsync(
        IApplicationDbContext db, ITokenService tokens, Domain.Entities.User user, CancellationToken ct)
    {
        var mustChange = user.MustChangePassword;
        var accessToken = tokens.IssueAccessToken(user, mustChange);
        var refreshToken = tokens.GenerateRefreshToken();
        db.UserCredentials.Add(new UserCredential
        {
            UserId = user.Id,
            TokenHash = tokens.HashToken(refreshToken),
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });
        await db.SaveChangesAsync(ct);
        return new AuthTokenResult(true,
            AccessToken: accessToken,
            RefreshToken: refreshToken,
            RefreshExpiresAt: DateTime.UtcNow.AddDays(7),
            MustChangePassword: mustChange);
    }
}

/// <summary>[AUTH Phase 3] GET /auth/passkeys/status (anonymous — login page gates its button on this).</summary>
public record GetPasskeyStatusQuery : IRequest<bool>;

public class GetPasskeyStatusQueryHandler(IWebAuthnService webAuthn)
    : IRequestHandler<GetPasskeyStatusQuery, bool>
{
    public Task<bool> Handle(GetPasskeyStatusQuery request, CancellationToken cancellationToken)
        => webAuthn.IsEnabledAsync(cancellationToken);
}

/// <summary>GET /auth/passkeys — the current user's registered passkeys.</summary>
public record ListPasskeysQuery(Guid CurrentUserId) : IRequest<List<PasskeyDto>>;

public class ListPasskeysQueryHandler(IApplicationDbContext context)
    : IRequestHandler<ListPasskeysQuery, List<PasskeyDto>>
{
    public Task<List<PasskeyDto>> Handle(ListPasskeysQuery request, CancellationToken cancellationToken)
        => context.UserPasskeys.AsNoTracking()
            .Where(p => p.UserId == request.CurrentUserId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PasskeyDto(p.Id, p.Name, p.CreatedAt, p.LastUsedAt))
            .ToListAsync(cancellationToken);
}

/// <summary>
/// POST /auth/passkeys/register/options — first half of the registration ceremony for the
/// CURRENT user (flag-gated: 403 PASSKEYS_DISABLED when off).
/// </summary>
public record GetPasskeyRegisterOptionsQuery(Guid CurrentUserId) : IRequest<string>;

public class GetPasskeyRegisterOptionsQueryHandler(IApplicationDbContext context, IWebAuthnService webAuthn)
    : IRequestHandler<GetPasskeyRegisterOptionsQuery, string>
{
    public async Task<string> Handle(GetPasskeyRegisterOptionsQuery request, CancellationToken cancellationToken)
    {
        var user = await context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.CurrentUserId && u.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("USER_NOT_FOUND");
        return await webAuthn.BeginRegistrationAsync(user, cancellationToken);
    }
}

/// <summary>
/// POST /auth/passkeys/register — second half: verify attestation + persist. ILoggableCommand
/// (ActionLog: who registered a passkey — security-relevant; never contains credential bytes).
/// </summary>
public record CompletePasskeyRegistrationCommand(Guid CurrentUserId, string AttestationJson, string? Name)
    : IRequest<RegisterPasskeyResult>, ILoggableCommand<RegisterPasskeyResult>
{
    public ActionLogEntry? BuildLogEntry(RegisterPasskeyResult response)
    {
        if (!response.Success || response.Passkey == null) return null;
        return new ActionLogEntry
        {
            ItemType = ItemType.User,
            ItemId = CurrentUserId,
            ActionType = ActionType.Create,
            CreatedBy = CurrentUserId,
            CompanyId = null,
            Note = $"Đăng ký passkey \"{response.Passkey.Name}\""
        };
    }
}

public record RegisterPasskeyResult(bool Success, string? ErrorCode = null, PasskeyDto? Passkey = null);

public class CompletePasskeyRegistrationCommandValidator : AbstractValidator<CompletePasskeyRegistrationCommand>
{
    public CompletePasskeyRegistrationCommandValidator()
    {
        RuleFor(x => x.AttestationJson).NotEmpty();
        RuleFor(x => x.Name).MaximumLength(100);
    }
}

public class CompletePasskeyRegistrationCommandHandler(IApplicationDbContext context, IWebAuthnService webAuthn)
    : IRequestHandler<CompletePasskeyRegistrationCommand, RegisterPasskeyResult>
{
    public async Task<RegisterPasskeyResult> Handle(CompletePasskeyRegistrationCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var passkey = await webAuthn.CompleteRegistrationAsync(
                request.CurrentUserId, request.AttestationJson, request.Name, cancellationToken);
            return new RegisterPasskeyResult(true, Passkey: new PasskeyDto(passkey.Id, passkey.Name!, passkey.CreatedAt, passkey.LastUsedAt));
        }
        catch (InvalidOperationException ex) when (ex.Message is "PASSKEY_REGISTRATION_EXPIRED" or "PASSKEY_MALFORMED_RESPONSE" or "PASSKEY_ALREADY_REGISTERED" or "PASSKEY_VERIFICATION_FAILED")
        {
            return new RegisterPasskeyResult(false, ex.Message);
        }
    }
}

/// <summary>
/// POST /auth/passkeys/login/options — first half of the assertion (login) ceremony. Anonymous.
/// Username optional: null → discoverable (username-less) login. Flag-gated via result.
/// </summary>
public record GetPasskeyLoginOptionsQuery(string? Username) : IRequest<PasskeyLoginOptionsResult>;

public record PasskeyLoginOptionsResult(bool Enabled, string? OptionsJson = null);

public class GetPasskeyLoginOptionsQueryHandler(IWebAuthnService webAuthn)
    : IRequestHandler<GetPasskeyLoginOptionsQuery, PasskeyLoginOptionsResult>
{
    public async Task<PasskeyLoginOptionsResult> Handle(GetPasskeyLoginOptionsQuery request, CancellationToken cancellationToken)
    {
        if (!await webAuthn.IsEnabledAsync(cancellationToken))
            return new PasskeyLoginOptionsResult(false);
        var usernameLower = string.IsNullOrWhiteSpace(request.Username)
            ? null
            : request.Username.Trim().ToLowerInvariant();
        return new PasskeyLoginOptionsResult(true, await webAuthn.BeginAssertionAsync(usernameLower, cancellationToken));
    }
}

/// <summary>
/// POST /auth/passkeys/login — second half: verify assertion, then issue the SAME token session
/// as password login (access JWT + refresh row; mustChange honored). NOT ILoggableCommand —
/// sign-in activity lives in auth_login_attempts (same decision as password login, §11.2).
/// Brute-force: recorded per username when known; the flag-off case is rejected BEFORE anything.
/// </summary>
public record VerifyPasskeyAssertionCommand(string AssertionJson, string IpAddress)
    : IRequest<AuthTokenResult>;

public class VerifyPasskeyAssertionCommandHandler(IApplicationDbContext context, IWebAuthnService webAuthn, ITokenService tokens)
    : IRequestHandler<VerifyPasskeyAssertionCommand, AuthTokenResult>
{
    public async Task<AuthTokenResult> Handle(VerifyPasskeyAssertionCommand request, CancellationToken cancellationToken)
    {
        if (!await webAuthn.IsEnabledAsync(cancellationToken))
            return new AuthTokenResult(false, "PASSKEYS_DISABLED");

        PasskeyLoginIdentity identity;
        try
        {
            identity = await webAuthn.CompleteAssertionAsync(request.AssertionJson, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message is "PASSKEY_ASSERTION_EXPIRED" or "PASSKEY_MALFORMED_RESPONSE" or "INVALID_CREDENTIALS")
        {
            return new AuthTokenResult(false, "INVALID_CREDENTIALS");
        }

        // Same brute-force audit surface as password login: record the SUCCESS attempt for the
        // username (resets the per-username counter — a successful passkey login proves identity).
        await context.AuthLoginAttempts.AddAsync(new AuthLoginAttempt
        {
            Username = identity.User.Username.ToLowerInvariant(),
            IpAddress = request.IpAddress ?? string.Empty,
            Success = true,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return await AuthSessionIssuer.IssueAsync(context, tokens, identity.User, cancellationToken);
    }
}

/// <summary>DELETE /auth/passkeys/{id} — the current user removes one of their own passkeys.</summary>
public record DeletePasskeyCommand(Guid CurrentUserId, Guid PasskeyId)
    : IRequest<AuthSimpleResult>, ILoggableCommand<AuthSimpleResult>
{
    public ActionLogEntry? BuildLogEntry(AuthSimpleResult response)
    {
        if (!response.Success) return null;
        return new ActionLogEntry
        {
            ItemType = ItemType.User,
            ItemId = CurrentUserId,
            ActionType = ActionType.Delete,
            CreatedBy = CurrentUserId,
            CompanyId = null,
            Note = "Xóa passkey đã đăng ký"
        };
    }
}

public class DeletePasskeyCommandHandler(IApplicationDbContext context)
    : IRequestHandler<DeletePasskeyCommand, AuthSimpleResult>
{
    public async Task<AuthSimpleResult> Handle(DeletePasskeyCommand request, CancellationToken cancellationToken)
    {
        var passkey = await context.UserPasskeys
            .FirstOrDefaultAsync(p => p.Id == request.PasskeyId && p.UserId == request.CurrentUserId, cancellationToken);
        if (passkey == null)
            return new AuthSimpleResult(false, "RESOURCE_NOT_FOUND"); // hide existence of other users' passkeys

        context.UserPasskeys.Remove(passkey);
        await context.SaveChangesAsync(cancellationToken);
        return new AuthSimpleResult(true);
    }
}
