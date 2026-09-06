using aspire_react.Server.Domain.Interfaces;
using Microsoft.AspNetCore.Http;

namespace aspire_react.Server.Infrastructure.Authentication;

/// <summary>
/// [AUTH Phase 1] Refresh-token cookie mechanics (AUTH_MIGRATION_PLAYBOOK §4.2 — Option A
/// same-origin): httpOnly + Secure + SameSite=Lax + Path=/api/v1/auth (only refresh/logout
/// endpoints ever read it) + 7-day Max-Age. SameSite configurable via Auth:Cookie:SameSite
/// (default Lax; prod can set Lax explicitly — same-origin through the nginx proxy).
/// </summary>
public class AuthCookieService : IAuthCookieService
{
    public const string CookieName = "refreshToken";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly string _sameSite;

    public AuthCookieService(IHttpContextAccessor httpContextAccessor, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _httpContextAccessor = httpContextAccessor;
        _sameSite = configuration["Auth:Cookie:SameSite"] ?? "Lax";
    }

    private HttpContext? HttpContext => _httpContextAccessor.HttpContext;

    public void SetRefreshCookie(string rawRefreshToken, DateTime expiresAt)
    {
        var context = HttpContext ?? throw new InvalidOperationException("No HTTP context — cookie can only be set during a request.");
        context.Response.Cookies.Append(CookieName, rawRefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = ParseSameSite(_sameSite),
            Path = "/api/v1/auth",
            MaxAge = expiresAt - DateTimeOffset.UtcNow
        });
    }

    public string? GetRefreshCookie()
        => HttpContext?.Request.Cookies[CookieName];

    public void ClearRefreshCookie()
    {
        var context = HttpContext ?? throw new InvalidOperationException("No HTTP context — cookie can only be cleared during a request.");
        context.Response.Cookies.Delete(CookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = ParseSameSite(_sameSite),
            Path = "/api/v1/auth"
        });
    }

    private static SameSiteMode ParseSameSite(string value) => value.ToLowerInvariant() switch
    {
        "none" => SameSiteMode.None,
        "strict" => SameSiteMode.Strict,
        _ => SameSiteMode.Lax
    };
}
