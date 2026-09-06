using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [AUTH Phase 1] PasswordHasherService + TokenService unit tests — PBKDF2 hash/verify contract
/// and the self-signed JWT lifecycle (claims golden-strategy, limited pwd_change token, signature
/// rejection, refresh-token generation/hashing determinism).
/// </summary>
public class AuthInfrastructureTests
{
    private static IConfiguration Config()
    {
        // Deterministic test key (≥ 32 bytes for HS256). Production value comes from user-secrets.
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Issuer"] = "test-issuer",
                ["Auth:Audience"] = "test-audience",
                ["Auth:SigningKey"] = "unit-test-signing-key-0123456789abcdef0123456789abcdef"
            })
            .Build();
    }

    private static User NewUser(bool super = false) => new()
    {
        Username = "authuser",
        Email = "authuser@test.local",
        FirstName = "Auth",
        LastName = "User",
        IsSuperUser = super,
        IsActive = true
    };

    // ==================== PasswordHasherService ====================

    [Fact]
    public void Hash_ProducesStorableHash_VerifiesRoundtrip()
    {
        var svc = new PasswordHasherService();
        var hash = svc.Hash("Tr0ub4dour&3");

        Assert.NotEqual("Tr0ub4dour&3", hash);             // never plaintext
        Assert.Equal(PasswordVerifyResult.Success, svc.Verify("Tr0ub4dour&3", hash));
        Assert.Equal(PasswordVerifyResult.Failed, svc.Verify("wrong-password", hash));
    }

    [Fact]
    public void Hash_SamePassword_DifferentSalts()
    {
        var svc = new PasswordHasherService();
        Assert.NotEqual(svc.Hash("same-password"), svc.Hash("same-password")); // salted
    }

    // ==================== TokenService ====================

    [Fact]
    public void IssueAccessToken_CarriesGoldenStrategyClaims()
    {
        var svc = new TokenService(Config(), NullLogger<TokenService>.Instance);
        var user = NewUser(super: true);
        user.Id = Guid.NewGuid();

        var token = svc.IssueAccessToken(user, mustChangePassword: false);
        var principal = svc.ValidateAccessToken(token);

        Assert.NotNull(principal);
        Assert.Equal(user.Id.ToString(), principal!.FindFirst("local_user_id")?.Value);
        Assert.Equal("authuser", principal.FindFirst("preferred_username")?.Value);
        Assert.Equal("authuser@test.local", principal.FindFirst("email")?.Value);
        // Superuser mirror claims — PermissionHandler + frontend isSuperUser() read these.
        Assert.Equal("superuser", principal.FindFirst("permission")?.Value);
        Assert.Contains("superuser", principal.FindFirst("realm_access")?.Value ?? string.Empty);
    }

    [Fact]
    public void IssueAccessToken_MustChangePassword_LimitedToken()
    {
        var svc = new TokenService(Config(), NullLogger<TokenService>.Instance);
        var principal = svc.ValidateAccessToken(svc.IssueAccessToken(NewUser(), mustChangePassword: true));

        Assert.NotNull(principal);
        Assert.Equal("1", principal!.FindFirst(TokenService.MustChangePasswordClaim)?.Value);
    }

    [Fact]
    public void ValidateAccessToken_RejectsTamperedAndForeignTokens()
    {
        var svc = new TokenService(Config(), NullLogger<TokenService>.Instance);
        var token = svc.IssueAccessToken(NewUser(), false);

        // Tampered payload (flip a char in the payload segment).
        var parts = token.Split('.');
        parts[1] = parts[1] == "abc" ? "abd" : "abc";
        var tampered = string.Join('.', parts);
        Assert.Null(svc.ValidateAccessToken(tampered));

        // Different signing key (foreign issuer) must fail.
        var foreignConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Issuer"] = "test-issuer",
                ["Auth:Audience"] = "test-audience",
                ["Auth:SigningKey"] = "completely-different-key-99887766554433221100"
            })
            .Build();
        var foreign = new TokenService(foreignConfig, NullLogger<TokenService>.Instance);
        Assert.Null(svc.ValidateAccessToken(foreign.IssueAccessToken(NewUser(), false)));
    }

    [Fact]
    public void RefreshToken_RawVsHash_DeterministicAndDistinct()
    {
        var svc = new TokenService(Config(), NullLogger<TokenService>.Instance);
        var raw = svc.GenerateRefreshToken();

        Assert.NotEqual(raw, svc.HashToken(raw));               // hash ≠ raw
        Assert.Equal(svc.HashToken(raw), svc.HashToken(raw));   // deterministic
        Assert.NotEqual(svc.GenerateRefreshToken(), raw);       // fresh randomness each call
    }
}
