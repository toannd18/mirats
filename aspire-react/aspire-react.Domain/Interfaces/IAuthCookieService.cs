namespace aspire_react.Server.Domain.Interfaces;

/// <summary>
/// [AUTH Phase 1] Refresh-token cookie contract — wraps the HTTP-only cookie mechanics
/// (httpOnly + Secure + SameSite=Lax + Path=/api/v1/auth per AUTH_MIGRATION_PLAYBOOK §4.2,
/// Option A same-origin). Implementation lives in Infrastructure/Authentication (framework-heavy);
/// controllers call the contract — the cookie VALUE is produced/consumed by TokenService/handlers.
/// </summary>
public interface IAuthCookieService
{
    /// <summary>Sets the refresh-token cookie (httpOnly, Secure, SameSite=Lax, Path=/api/v1/auth,
    /// Max-Age 7 days). SameSite configurable via Auth:Cookie:SameSite (default Lax).</summary>
    void SetRefreshCookie(string rawRefreshToken, DateTime expiresAt);

    /// <summary>Reads the raw refresh token from the request cookie (null when absent).</summary>
    string? GetRefreshCookie();

    /// <summary>Clears the refresh-token cookie (logout / reuse-detection containment).</summary>
    void ClearRefreshCookie();
}
