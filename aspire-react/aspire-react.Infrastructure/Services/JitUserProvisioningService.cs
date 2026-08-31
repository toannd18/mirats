using System.Security.Claims;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace aspire_react.Server.Infrastructure.Services;

/// <summary>
/// JIT (just-in-time) local user provisioning from a validated Keycloak token.
/// <para>
/// Extracted from the JWT <c>OnTokenValidated</c> handler (which used to contain this business
/// logic inline) so it can be unit-tested independently. Behavior is identical to the original
/// inline block — it is called per-request with a scoped <see cref="AppDbContext"/>.
/// </para>
/// </summary>
public interface IJitUserProvisioningService
{
    /// <summary>
    /// Ensures a local <see cref="User"/> exists for the token's <c>preferred_username</c>, syncs
    /// name/email if changed, and returns the local user id (or null when no username is present).
    /// The caller (JWT handler) stamps the <c>local_user_id</c> claim onto the principal.
    /// </summary>
    Task<Guid?> ProvisionAsync(ClaimsPrincipal? principal, CancellationToken cancellationToken = default);
}

public class JitUserProvisioningService : IJitUserProvisioningService
{
    private readonly AppDbContext _db;

    public JitUserProvisioningService(AppDbContext db) => _db = db;

    /// <summary>Returns the first non-empty value for a claim, trying the short OIDC name first then the
    /// mapped <c>ClaimTypes</c> URI (handles ASP.NET's default MapInboundClaims). Returns null if neither is present.</summary>
    private static string? FirstClaim(ClaimsPrincipal? principal, string oidcName, string mappedName)
    {
        var shortVal = principal?.FindFirst(oidcName)?.Value;
        if (!string.IsNullOrEmpty(shortVal)) return shortVal;
        return principal?.FindFirst(mappedName)?.Value;
    }

    public async Task<Guid?> ProvisionAsync(ClaimsPrincipal? principal, CancellationToken cancellationToken = default)
    {
        var username = principal?.FindFirst("preferred_username")?.Value;
        // Task BACKLOG-2 (Mục 1): ASP.NET's default MapInboundClaims maps the OIDC "email"/"given_name"/
        // "family_name" claims to long URIs (ClaimTypes.Email/GivenName/Surname), so FindFirst("email")
        // returns null even though Keycloak sends them. Read BOTH the short OIDC name AND the mapped
        // ClaimTypes.* name so this works whether or not MapInboundClaims is disabled. preferred_username
        // is not part of the inbound-claim mapping table, so it is read by its short name only.
        var email = FirstClaim(principal, "email", ClaimTypes.Email);
        var firstName = FirstClaim(principal, "given_name", ClaimTypes.GivenName);
        var lastName = FirstClaim(principal, "family_name", ClaimTypes.Surname);

        if (string.IsNullOrEmpty(username))
            return null;

        // Check if user exists locally by Username or Email
        var localUser = await _db.Users.FirstOrDefaultAsync(
            u => u.Username == username || (!string.IsNullOrEmpty(email) && u.Email == email),
            cancellationToken);

        if (localUser == null)
        {
            // Create new local user record.
            // ST10: stamp the local IsSuperUser flag from the Keycloak realm role (admin/superuser)
            // so a fresh realm-admin login is flagged as superuser locally too. The PermissionHandler
            // realm-role bypass still governs authorization, but the local flag stays consistent with
            // the source of truth.
            localUser = new User
            {
                Id = Guid.NewGuid(),
                Username = username,
                Email = email ?? $"{username}@placeholder.local",
                FirstName = firstName ?? string.Empty,
                LastName = lastName ?? string.Empty,
                IsActive = true,
                IsSuperUser = RealmAccessHelper.IsSuperUser(principal)
            };
            _db.Users.Add(localUser);

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Race on first login: the dashboard fires several authenticated requests
                // concurrently, each running JIT provisioning. Two (or more) of them can pass the
                // existence check before either commits, so one insert wins and the rest hit the
                // unique index (IX_users_Username / IX_users_Email). Roll back this attempt's
                // tracking state and adopt the row that won — idempotent, no lost updates.
                _db.ChangeTracker.Clear();
                localUser = await _db.Users.FirstOrDefaultAsync(
                    u => u.Username == username || (!string.IsNullOrEmpty(email) && u.Email == email),
                    cancellationToken);
                if (localUser == null)
                {
                    // Extremely unlikely: the winning insert was rolled back between the exception
                    // and our re-read. Retry once by recursing.
                    return await ProvisionAsync(principal, cancellationToken);
                }
            }
        }
        else
        {
            // Sync name/email from Keycloak if changed
            var changed = false;
            if (!string.IsNullOrEmpty(email) && localUser.Email != email)
            {
                localUser.Email = email;
                changed = true;
            }
            if (!string.IsNullOrEmpty(firstName) && localUser.FirstName != firstName)
            {
                localUser.FirstName = firstName;
                changed = true;
            }
            if (!string.IsNullOrEmpty(lastName) && localUser.LastName != lastName)
            {
                localUser.LastName = lastName;
                changed = true;
            }
            if (changed)
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        return localUser.Id;
    }
}
