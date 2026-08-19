using System.Security.Claims;
using System.Text.Json;

namespace aspire_react.Server.Infrastructure.Services;

/// <summary>
/// Centralized (EXACT) superuser detection from the JWT <c>realm_access</c> claim.
/// Keycloak serializes <c>realm_access</c> as a JSON string, e.g. {"roles":["default-roles-aspire-react","admin"]}.
/// We must match role names EXACTLY ("admin" / "superuser"). A substring check (e.g.
/// <c>realmAccess.Contains("admin")</c>) would wrongly escalate any realm role whose name merely
/// CONTAINS those substrings (e.g. "company-admin", "support-admin") to full superuser bypass.
/// Also honours the legacy "permission"="superuser" claim as the previous code did.
/// </summary>
public static class RealmAccessHelper
{
    /// <summary>True when the principal carries the Keycloak realm role "admin" or "superuser" (exact match).</summary>
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