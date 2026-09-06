using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace aspire_react.Server.Infrastructure.Authentication;

/// <summary>
/// [AUTH Phase 1] Self-signed JWT issuing + validation and refresh-token generation/hashing.
/// GOLDEN STRATEGY (AUTH_MIGRATION_PLAYBOOK §3): issued tokens carry the SAME claims Keycloak
/// used to issue (sub, preferred_username, email, given_name, family_name, local_user_id,
/// permission + realm_access for superusers) so all existing claim-reading code keeps working.
/// Signing key/issuer/audience come from Auth:* configuration (user-secrets/AppHost secret).
/// </summary>
public class TokenService : ITokenService
{
    public const string MustChangePasswordClaim = "pwd_change";

    static TokenService()
    {
        // GOLDEN STRATEGY requirement: issued claims must keep their SHORT OIDC names verbatim
        // (preferred_username, email, given_name, family_name...) — the default outbound map
        // rewrites e.g. "email" → ClaimTypes.Email URI which breaks FindFirst("email") parity
        // with the old Keycloak tokens.
        JwtSecurityTokenHandler.DefaultOutboundClaimTypeMap.Clear();
    }

    private readonly IConfiguration _configuration;
    private readonly ILogger<TokenService> _logger;

    public TokenService(IConfiguration configuration, ILogger<TokenService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    private string Issuer => _configuration["Auth:Issuer"] ?? "aspire-react";
    private string Audience => _configuration["Auth:Audience"] ?? "aspire-react-api";
    private byte[] SigningKeyBytes => Encoding.UTF8.GetBytes(
        _configuration["Auth:SigningKey"]
        ?? throw new InvalidOperationException("Auth:SigningKey is not configured (user-secrets/AppHost secret)."));

    private SymmetricSecurityKey SigningKey => new(SigningKeyBytes);

    public string IssueAccessToken(User user, bool mustChangePassword)
    {
        var now = DateTime.UtcNow;
        var expires = mustChangePassword ? now.AddMinutes(10) : now.AddMinutes(15);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new("preferred_username", user.Username),
            new("email", user.Email),
            new("given_name", user.FirstName ?? string.Empty),
            new("family_name", user.LastName ?? string.Empty),
            // Golden strategy: stamp the local id DIRECTLY at issuance (user exists in DB — no JIT needed).
            new("local_user_id", user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            // NOTE: deliberately ONLY short OIDC claim names — the default outbound claim-type map
            // rewrites ClaimTypes.* URIs and duplicate mapped/short pairs confuse FindFirst lookups.
        };

        if (mustChangePassword)
        {
            // Limited-scope token: middleware allows only /auth/password + /users/me.
            claims.Add(new Claim(MustChangePasswordClaim, "1"));
        }

        if (user.IsSuperUser)
        {
            // Mirror the Keycloak claims the PermissionHandler + frontend isSuperUser() read —
            // verbatim so existing authorization logic works unchanged during the migration.
            claims.Add(new Claim("permission", "superuser"));
            claims.Add(new Claim("realm_access", """{"roles":["superuser"]}"""));
        }

        var credentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public ClaimsPrincipal? ValidateAccessToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler
            {
                // Keep SHORT OIDC claim names verbatim on the way in as well (mirrors the
                // Keycloak-era JIT reader which handled both short and mapped names).
                MapInboundClaims = false
            };
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = Issuer,
                ValidateAudience = true,
                ValidAudience = Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = SigningKey,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            };
            return handler.ValidateToken(token, parameters, out _);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            _logger.LogDebug("Access token validation failed: {Reason}", ex.GetType().Name);
            return null;
        }
    }

    public string GenerateRefreshToken()
    {
        // 256 bits of cryptographic randomness → base64url (43 chars, URL-safe for cookie transport).
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
