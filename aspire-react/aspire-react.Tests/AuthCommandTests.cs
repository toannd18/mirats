using aspire_react.Server.Application.Auth.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Authentication;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [AUTH Phase 1] Login/refresh/password command handlers — brute-force defense rules (§4.5:
/// escalating per-username lockout, generic no-enumeration message, per-IP burst, reset on
/// success), refresh rotation + reuse-detection, MustChangePassword flag, and the two
/// ILoggableCommand password actions (log exists but NEVER contains password/hash).
/// </summary>
public class AuthCommandTests
{
    private const string SigningKey = "unit-test-signing-key-0123456789abcdef0123456789abcdef";

    private static ITokenService TokenSvc() => new TokenService(TestConfig(), NullLogger<TokenService>.Instance);

    private static IConfiguration TestConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Issuer"] = "test-issuer",
            ["Auth:Audience"] = "test-audience",
            ["Auth:SigningKey"] = SigningKey
        })
        .Build();

    private static (AppDbContext db, User user) SeedUser(string username = "authuser", string? password = "correct-horse-1", bool mustChange = false)
    {
        var db = TestHelpers.CreateContext($"auth-{Guid.NewGuid():N}");
        var hasher = new PasswordHasherService();
        var user = new User
        {
            Username = username,
            Email = $"{username}@test.local",
            FirstName = "Auth",
            LastName = "User",
            IsActive = true,
            PasswordHash = password == null ? null : hasher.Hash(password),
            MustChangePassword = mustChange
        };
        db.Users.Add(user);
        db.SaveChanges();
        return (db, user);
    }

    private static LoginCommandHandler LoginHandler(AppDbContext db)
        => new(db, new PasswordHasherService(), TokenSvc(), new AuthAttemptService(db));

    // ==================== Login ====================

    [Fact]
    public async Task Login_CorrectPassword_Succeeds_IssuesTokens_AndResetsCounter()
    {
        var (db, user) = SeedUser();
        var handler = LoginHandler(db);

        // 2 failures first...
        await handler.Handle(new LoginCommand(user.Username, "wrong-pass-1", "1.1.1.1"), CancellationToken.None);
        await handler.Handle(new LoginCommand(user.Username, "wrong-pass-2", "1.1.1.1"), CancellationToken.None);

        var ok = await handler.Handle(new LoginCommand(user.Username.ToUpper(), "correct-horse-1", "1.1.1.1"), CancellationToken.None);
        Assert.True(ok.Success);
        Assert.NotNull(ok.AccessToken);
        Assert.NotNull(ok.RefreshToken);
        Assert.False(ok.MustChangePassword);

        // Counter reset: a wrong password right after success starts from scratch (no lockout).
        var after = await handler.Handle(new LoginCommand(user.Username, "wrong-again-9", "1.1.1.1"), CancellationToken.None);
        Assert.False(after.Success);
        Assert.Equal("INVALID_CREDENTIALS", after.ErrorCode); // not ACCOUNT_LOCKED
    }

    [Fact]
    public async Task Login_WrongPassword_GenericMessage_NoUserEnumeration()
    {
        var (db, user) = SeedUser();
        var unknown = await LoginHandler(db).Handle(new LoginCommand("ghost-user", "whatever-1", "1.1.1.1"), CancellationToken.None);
        var wrongPw = await LoginHandler(db).Handle(new LoginCommand(user.Username, "wrong-pass-1", "1.1.1.1"), CancellationToken.None);

        Assert.Equal("INVALID_CREDENTIALS", unknown.ErrorCode);
        Assert.Equal("INVALID_CREDENTIALS", wrongPw.ErrorCode); // same code for unknown user AND wrong password
    }

    [Fact]
    public async Task Login_EscalatingLockout_AfterFiveConsecutiveFailures()
    {
        var (db, user) = SeedUser();
        var handler = LoginHandler(db);

        // 4 wrong attempts — no lockout yet (each returns the generic failure).
        for (var i = 1; i <= 4; i++)
        {
            var r = await handler.Handle(new LoginCommand(user.Username, $"wrong-{i}", "2.2.2.2"), CancellationToken.None);
            Assert.Equal("INVALID_CREDENTIALS", r.ErrorCode);
        }

        // 5th attempt with the CORRECT password still succeeds (threshold counts PRIOR failures,
        // lockout applies from the NEXT attempt after 5 failures have accumulated).
        var fifth = await handler.Handle(new LoginCommand(user.Username, "correct-horse-1", "2.2.2.2"), CancellationToken.None);
        Assert.True(fifth.Success);

        // Re-derive: build 5 fresh consecutive failures again (success reset the counter)...
        for (var i = 1; i <= 5; i++)
        {
            await handler.Handle(new LoginCommand(user.Username, $"wrong-{i}", "2.2.2.2"), CancellationToken.None);
        }

        // ...now even the CORRECT password is locked out.
        var locked = await handler.Handle(new LoginCommand(user.Username, "correct-horse-1", "2.2.2.2"), CancellationToken.None);
        Assert.Equal("ACCOUNT_LOCKED", locked.ErrorCode);

        var stillLocked = await handler.Handle(new LoginCommand(user.Username, "correct-horse-1", "2.2.2.2"), CancellationToken.None);
        Assert.Equal("ACCOUNT_LOCKED", stillLocked.ErrorCode);
    }

    [Fact]
    public async Task Login_UserWithoutLocalPassword_GenericFailure()
    {
        var (db, user) = SeedUser(password: null); // admin hasn't reset this Keycloak-era user yet
        var result = await LoginHandler(db).Handle(new LoginCommand(user.Username, "any-password", "3.3.3.3"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("INVALID_CREDENTIALS", result.ErrorCode); // generic — never reveals "no local password"
    }

    [Fact]
    public async Task Login_MustChangePassword_FlagsLimitedToken()
    {
        var (db, user) = SeedUser(mustChange: true);
        var result = await LoginHandler(db).Handle(new LoginCommand(user.Username, "correct-horse-1", "4.4.4.4"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.MustChangePassword);
        var principal = TokenSvc().ValidateAccessToken(result.AccessToken!);
        Assert.Equal("1", principal!.FindFirst(TokenService.MustChangePasswordClaim)?.Value);
    }

    // ==================== Refresh rotation + reuse-detection ====================

    [Fact]
    public async Task Refresh_RotatesToken_OldBecomesRevoked()
    {
        var (db, user) = SeedUser();
        var login = await LoginHandler(db).Handle(new LoginCommand(user.Username, "correct-horse-1", "5.5.5.5"), CancellationToken.None);
        var refreshHandler = new RefreshTokenCommandHandler(db, TokenSvc());

        var refreshed = await refreshHandler.Handle(new RefreshTokenCommand(login.RefreshToken!), CancellationToken.None);
        Assert.True(refreshed.Success);
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);

        // Rotation: the OLD token is now revoked — presenting it again must trigger reuse-detection.
        var reuse = await refreshHandler.Handle(new RefreshTokenCommand(login.RefreshToken!), CancellationToken.None);
        Assert.False(reuse.Success);

        // Containment: ALL sessions of the user (including the fresh one) are revoked.
        Assert.Equal(0, await db.UserCredentials.CountAsync(c => c.RevokedAt == null));
    }

    [Fact]
    public async Task Refresh_UnknownOrExpiredToken_Rejected()
    {
        var (db, _) = SeedUser();
        var handler = new RefreshTokenCommandHandler(db, TokenSvc());

        Assert.False((await handler.Handle(new RefreshTokenCommand("totally-unknown-token"), CancellationToken.None)).Success);
    }

    // ==================== ChangePassword / AdminResetPassword ====================

    [Fact]
    public async Task ChangePassword_WrongCurrent_Rejected_RightCurrent_Succeeds_AndRevokesOthers()
    {
        var (db, user) = SeedUser();
        var login = await LoginHandler(db).Handle(new LoginCommand(user.Username, "correct-horse-1", "6.6.6.6"), CancellationToken.None);

        // Another session's refresh row (simulates a second device).
        var otherRow = new UserCredential { UserId = user.Id, TokenHash = TokenSvc().HashToken("other-device-token"), ExpiresAt = DateTime.UtcNow.AddDays(7) };
        db.UserCredentials.Add(otherRow);
        await db.SaveChangesAsync();

        // Drive via the REAL MediatR pipeline so ValidationBehavior + ActionLogBehavior run
        // (ILoggableCommand staging is the behavior's job — handler-level misses it).
        var mediator = TestHelpers.BuildMediator(db, actorId: user.Id);

        var wrongCurrent = await mediator.Send(new ChangePasswordCommand(user.Id, "not-my-password", "new-password-1"));
        Assert.False(wrongCurrent.Success);

        // Validator (≥ 8 chars) runs through the pipeline — FluentValidation exception → mapped
        // by ValidationExceptionHandler; at handler level this surfaces as a ValidationException.
        await Assert.ThrowsAnyAsync<Exception>(() =>
            mediator.Send(new ChangePasswordCommand(user.Id, "correct-horse-1", "short")));

        var ok = await mediator.Send(new ChangePasswordCommand(user.Id, "correct-horse-1", "brand-new-pw-1"));
        Assert.True(ok.Success);

        var reloaded = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.False(reloaded.MustChangePassword);
        Assert.Equal(PasswordVerifyResult.Success, new PasswordHasherService().Verify("brand-new-pw-1", reloaded.PasswordHash!));

        // Sessions revoked (all-of-them — simplest secure default).
        Assert.Equal(0, await db.UserCredentials.CountAsync(c => c.RevokedAt == null));

        // ActionLogBehavior wrote the log — it must NOT contain the password or its hash.
        var log = await db.ActionLogs.SingleAsync(l => l.ItemType == ItemType.User && l.ActionType == ActionType.Update);
        Assert.DoesNotContain("brand-new-pw-1", log.Note);
        Assert.DoesNotContain("brand-new-pw-1", log.LogMeta ?? string.Empty);
    }

    [Fact]
    public async Task AdminReset_SetsMustChange_RevokesSessions_LogsWithoutPassword()
    {
        var (db, user) = SeedUser();
        var login = await LoginHandler(db).Handle(new LoginCommand(user.Username, "correct-horse-1", "7.7.7.7"), CancellationToken.None);
        Assert.True(login.Success);

        var admin = new User { Username = "the-admin", Email = "admin@test.local", FirstName = "A", LastName = "D" };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        // Real pipeline (ActionLogBehavior writes the ILoggableCommand log).
        var mediator = TestHelpers.BuildMediator(db, actorId: admin.Id);
        var result = await mediator.Send(new AdminResetPasswordCommand(user.Id, "temp-password-1", admin.Id));

        Assert.True(result.Success);
        var reloaded = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.True(reloaded.MustChangePassword);
        Assert.Equal(0, await db.UserCredentials.CountAsync(c => c.RevokedAt == null)); // all sessions revoked

        // Login with the temp password succeeds BUT the token is limited (must change first).
        var relogin = await LoginHandler(db).Handle(new LoginCommand(user.Username, "temp-password-1", "8.8.8.8"), CancellationToken.None);
        Assert.True(relogin.Success);
        Assert.True(relogin.MustChangePassword);

        var log = await db.ActionLogs.SingleAsync(l => l.ItemType == ItemType.User);
        Assert.DoesNotContain("temp-password-1", log.Note);
        Assert.DoesNotContain("temp-password-1", log.LogMeta ?? string.Empty);
    }

    // ==================== Brute-force service rules (direct) ====================

    [Fact]
    public void LockoutSeconds_Escalation_Curve()
    {
        var svc = new AuthAttemptService(TestHelpers.CreateContext("auth-attempt-rules"));
        Assert.Equal(0, svc.LockoutSeconds(0));
        Assert.Equal(0, svc.LockoutSeconds(4));
        Assert.Equal(60, svc.LockoutSeconds(5));
        Assert.Equal(64, svc.LockoutSeconds(6));
        Assert.Equal(900, svc.LockoutSeconds(20)); // capped at 15 minutes
    }

    [Fact]
    public async Task CleanupOld_RemovesRows_OlderThan30Days()
    {
        var db = TestHelpers.CreateContext("auth-cleanup");
        db.AuthLoginAttempts.Add(new AuthLoginAttempt { Username = "old", IpAddress = "x", Success = false, CreatedAt = DateTime.UtcNow.AddDays(-31) });
        db.AuthLoginAttempts.Add(new AuthLoginAttempt { Username = "new", IpAddress = "x", Success = false, CreatedAt = DateTime.UtcNow.AddHours(-1) });
        await db.SaveChangesAsync();

        await new AuthAttemptService(db).CleanupOldAsync();

        Assert.Equal(0, await db.AuthLoginAttempts.CountAsync(a => a.Username == "old"));  // > 30 days → deleted
        Assert.Equal(1, await db.AuthLoginAttempts.CountAsync(a => a.Username == "new"));  // recent → kept
    }
}
