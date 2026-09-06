using System.Security.Claims;

namespace aspire_react.Server.Domain.Interfaces;

/// <summary>
/// [AUTH Phase 1] Self-signed JWT + refresh-token lifecycle contract. Issued tokens carry the
/// SAME claims Keycloak used to issue (golden strategy of the migration — see
/// AUTH_MIGRATION_PLAYBOOK §3): sub, preferred_username, email, given_name, family_name,
/// local_user_id, and for superusers permission/realm_access — so the existing claim-reading
/// code keeps working untouched. Implementation (Infrastructure/Authentication) owns the
/// framework-heavy JwtSecurityToken work.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Issues a signed 15-minute access JWT for the user. When <paramref name="mustChangePassword"/>
    /// is true the token is LIMITED-SCOPE: claim pwd_change=1 and a 10-minute TTL — middleware
    /// blocks everything except /auth/password and /users/me.
    /// </summary>
    string IssueAccessToken(Domain.Entities.User user, bool mustChangePassword);

    /// <summary>
    /// Validates a self-signed access JWT and returns its principal, or null when invalid
    /// (bad signature, wrong issuer/audience, expired). Lifetime is validated (skew 1 minute).
    /// </summary>
    ClaimsPrincipal? ValidateAccessToken(string token);

    /// <summary>Generates a new cryptographically-random refresh token (raw value — give to the
    /// client only; persist only <see cref="HashToken"/> of it).</summary>
    string GenerateRefreshToken();

    /// <summary>SHA-256 of the raw refresh token — the ONLY form ever persisted.</summary>
    string HashToken(string rawToken);
}
