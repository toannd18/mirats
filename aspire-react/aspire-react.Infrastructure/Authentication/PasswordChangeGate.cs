using System.Security.Claims;
using aspire_react.Server.Domain.Interfaces;

namespace aspire_react.Server.Infrastructure.Authentication;

/// <summary>
/// [AUTH Phase 1 gap fix — phát hiện qua Phase 2 live-verify] Enforces the MustChangePassword
/// flow designed in §4.4: a session carrying pwd_change=1 may ONLY touch
/// /api/v1/users/me (profile) and /api/v1/auth/password (the change itself) — EVERY other
/// endpoint returns 403 MUST_CHANGE_PASSWORD until the password is changed.
/// Implemented as a pure static decision (unit-testable) and wired into OnTokenValidated of
/// the "App" scheme. Path matching is EXACT (trailing-slash normalized) — /api/v1/users/me
/// must never accidentally match /api/v1/users/{id} or any other segment-based route.
/// </summary>
public static class PasswordChangeGate
{
    private static readonly string[] AllowedPaths = ["/api/v1/users/me", "/api/v1/auth/password"];

    /// <summary>True khi session mang claim pwd_change=1 (MustChangePassword đang active).</summary>
    public static bool IsLimited(ClaimsPrincipal? principal)
        => principal?.FindFirst(TokenService.MustChangePasswordClaim)?.Value == "1";

    /// <summary>EXACT path match (trailing slash normalized) — không dùng StartsWithSegments
    /// để tránh khớp lỏng /api/v1/users/me với /api/v1/users/{id}.</summary>
    public static bool IsAllowedPath(PathString path)
    {
        var value = path.Value ?? string.Empty;
        if (value.Length > 1 && value.EndsWith('/')) value = value.TrimEnd('/');
        return AllowedPaths.Contains(value, StringComparer.Ordinal);
    }

    /// <summary>Quyết định chặn: limited session + path ngoài allowlist → chặn (403).</summary>
    public static bool ShouldBlock(ClaimsPrincipal? principal, PathString path)
        => IsLimited(principal) && !IsAllowedPath(path);
}
