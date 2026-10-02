using System.Security.Claims;
using System.Text.Json;

namespace aspire_react.Server.Infrastructure.Authentication;

/// <summary>
/// [AUTH Phase 5, renamed from RealmAccessHelper] Centralized (EXACT) superuser detection from
/// the JWT claims. The local TokenService emits the SAME wire claims the migration's golden
/// strategy preserved: <c>permission</c>="superuser" and <c>realm_access</c>={"roles":["superuser"]}
/// (the frontend isSuperUser() reads the same shape). Roles must match EXACTLY ("admin"/
/// "superuser") — a substring check would wrongly escalate roles like "company-admin".
///
/// [FIX-N16 2026-10-02] Moved from Infrastructure/Services to Infrastructure/Authentication: it is
/// a claim/identity helper, so it belongs next to TokenService (which mints exactly these claims —
/// that is also where CLAUDE.md said it lived).
/// </summary>
public static class SuperuserClaims
{
    /// <summary>True when the principal carries the exact role "admin" or "superuser".</summary>
    public static bool IsSuperUser(ClaimsPrincipal? user)
    {
        if (user == null) return false;
        if (user.HasClaim(c => c.Type == "permission" && c.Value == "superuser")) return true;

        var json = user.FindFirstValue("realm_access");
        if (string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("roles", out var roles)
                && roles.ValueKind == JsonValueKind.Array)
            {
                foreach (var role in roles.EnumerateArray())
                {
                    var name = role.GetString();
                    if (name == "admin" || name == "superuser") return true;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }
}
