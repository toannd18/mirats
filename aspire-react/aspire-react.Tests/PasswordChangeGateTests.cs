using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [AUTH Phase 1 gap fix] PasswordChangeGate — pwd_change=1 sessions are limited to EXACTLY
/// /api/v1/users/me and /api/v1/auth/password. Minimum 3 cases per approval + exactness cases
/// (trailing slash ok; /api/v1/users/me must NOT match /api/v1/users/{id}-style paths) and the
/// regression guard: regular sessions (no pwd_change flag) are never blocked.
/// </summary>
public class PasswordChangeGateTests
{
    private static ITokenService TokenSvc() => new TokenService(TestHelpers.AuthConfigForGate(), NullLogger<TokenService>.Instance);

    [Fact]
    public void LimitedToken_UsersMe_Allowed()
    {
        var principal = TokenSvc().ValidateAccessToken(TokenSvc().IssueAccessToken(TestHelpers.AuthGateUser(), mustChangePassword: true));
        Assert.True(PasswordChangeGate.IsLimited(principal));
        Assert.False(PasswordChangeGate.ShouldBlock(principal, new PathString("/api/v1/users/me")));
    }

    [Fact]
    public void LimitedToken_AuthPassword_Allowed()
    {
        var principal = TokenSvc().ValidateAccessToken(TokenSvc().IssueAccessToken(TestHelpers.AuthGateUser(), mustChangePassword: true));
        Assert.False(PasswordChangeGate.ShouldBlock(principal, new PathString("/api/v1/auth/password")));
    }

    [Fact]
    public void LimitedToken_AssetsEndpoint_Blocked()
    {
        var principal = TokenSvc().ValidateAccessToken(TokenSvc().IssueAccessToken(TestHelpers.AuthGateUser(), mustChangePassword: true));
        Assert.True(PasswordChangeGate.ShouldBlock(principal, new PathString("/api/v1/assets")));
    }

    [Fact]
    public void LimitedToken_DashboardSummary_Blocked()
    {
        var principal = TokenSvc().ValidateAccessToken(TokenSvc().IssueAccessToken(TestHelpers.AuthGateUser(), mustChangePassword: true));
        Assert.True(PasswordChangeGate.ShouldBlock(principal, new PathString("/api/v1/dashboard/summary")));
    }

    [Fact]
    public void LimitedTrailingSlash_UsersMe_StillAllowed_Normalization()
    {
        var principal = TokenSvc().ValidateAccessToken(TokenSvc().IssueAccessToken(TestHelpers.AuthGateUser(), mustChangePassword: true));
        Assert.False(PasswordChangeGate.ShouldBlock(principal, new PathString("/api/v1/users/me/")));
    }

    [Fact]
    public void Limited_UsersMeExactness_NoSegmentLeak()
    {
        var principal = TokenSvc().ValidateAccessToken(TokenSvc().IssueAccessToken(TestHelpers.AuthGateUser(), mustChangePassword: true));
        // EXACT match requirement: /api/v1/users/me must never match /api/v1/users/{id} paths.
        Assert.True(PasswordChangeGate.ShouldBlock(principal, new PathString("/api/v1/users/35a18d38-b955-4fdc-95ab-b98e49982fc4")));
        Assert.True(PasswordChangeGate.ShouldBlock(principal, new PathString("/api/v1/users")));
        Assert.True(PasswordChangeGate.ShouldBlock(principal, new PathString("/api/v1/users/me/history")));
    }

    [Fact]
    public void RegularToken_NoFlag_NeverBlocked()
    {
        var principal = TokenSvc().ValidateAccessToken(TokenSvc().IssueAccessToken(TestHelpers.AuthGateUser(), mustChangePassword: false));
        Assert.False(PasswordChangeGate.IsLimited(principal));
        Assert.False(PasswordChangeGate.ShouldBlock(principal, new PathString("/api/v1/assets")));
        Assert.False(PasswordChangeGate.ShouldBlock(principal, new PathString("/api/v1/dashboard/summary")));
    }

    [Fact]
    public void AnonymousPrincipal_NotGateScope()
    {
        // Anonymous requests are the job of [Authorize] (401) — the pwd_change gate is scoped
        // to AUTHENTICATED sessions only, so ShouldBlock(null, ...) must be False.
        Assert.False(PasswordChangeGate.ShouldBlock(null, new PathString("/api/v1/assets")));
    }
}
