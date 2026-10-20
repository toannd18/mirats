using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Infrastructure.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace aspire_react.Server.Infrastructure.Persistence;

/// <summary>
/// [READY GĐ0] First-admin bootstrap, extracted from <see cref="StartupDataSeeder"/> so the policy
/// is testable without <c>Database.Migrate()</c>.
///
/// The one-time <c>Auth:BootstrapAdminPassword</c> is validated ONLY when this code is about to
/// create the first admin on a blank database (≥ <see cref="MinimumPasswordLength"/> characters,
/// different from the username). An already-existing admin is never re-validated or overwritten —
/// a redeploy must not be blocked by a stale bootstrap secret in <c>.env</c>.
/// </summary>
public static class BootstrapAdminSeeder
{
    public const int MinimumPasswordLength = 12;

    public static void Ensure(AppDbContext db, IConfiguration config, ILogger logger)
    {
        var bootstrapPassword = config["Auth:BootstrapAdminPassword"];
        if (string.IsNullOrEmpty(bootstrapPassword)) return;

        var adminUsername = config["INITIAL_ADMIN_USERNAME"];
        if (string.IsNullOrWhiteSpace(adminUsername)) adminUsername = "admin";
        var adminEmail = config["INITIAL_ADMIN_EMAIL"];
        if (string.IsNullOrWhiteSpace(adminEmail)) adminEmail = "admin@localhost";

        var admin = db.Users.FirstOrDefault(u => u.Username.ToLower() == adminUsername.ToLower());
        if (admin == null)
        {
            if (db.Users.Any(u => u.Email == adminEmail))
            {
                logger.LogWarning(
                    "Bootstrap admin not created: email '{Email}' is already used by another user. " +
                    "Set INITIAL_ADMIN_EMAIL to a free address.", adminEmail);
                return;
            }

            // Policy applies only to the one-time creation path (blank DB / no admin yet).
            ValidateNewAdminPassword(bootstrapPassword, adminUsername);

            db.Users.Add(new User
            {
                Username = adminUsername,
                Email = adminEmail,
                FirstName = "System",
                LastName = "Admin",
                PasswordHash = new PasswordHasherService().Hash(bootstrapPassword),
                // One-time .env secret → force a password change at first login.
                MustChangePassword = true,
                IsSuperUser = true,
                IsActive = true
            });
            db.SaveChanges();
            logger.LogInformation(
                "Bootstrap admin '{Username}' created (local auth, must change password at first login).",
                adminUsername);
        }
        else if (string.IsNullOrEmpty(admin.PasswordHash))
        {
            // Existing row without a local password: seed the hash only, never validate/overwrite.
            admin.PasswordHash = new PasswordHasherService().Hash(bootstrapPassword);
            db.SaveChanges();
            logger.LogInformation("Bootstrap password seeded for existing admin '{Username}'.", adminUsername);
        }
    }

    /// <summary>
    /// Throws <see cref="BootstrapAdminConfigurationException"/> when the one-time password is too
    /// short or equals the username. Public for the startup policy tests.
    /// </summary>
    public static void ValidateNewAdminPassword(string password, string username)
    {
        if (password.Length < MinimumPasswordLength)
        {
            throw new BootstrapAdminConfigurationException(
                $"Auth:BootstrapAdminPassword must be at least {MinimumPasswordLength} characters when creating " +
                $"the first admin (configured value: {password.Length} characters). Set a stronger password in " +
                ".env and restart the server.");
        }

        if (string.Equals(password, username, StringComparison.OrdinalIgnoreCase))
        {
            throw new BootstrapAdminConfigurationException(
                "Auth:BootstrapAdminPassword must not be the same as INITIAL_ADMIN_USERNAME. " +
                "Set a different bootstrap password in .env and restart the server.");
        }
    }
}

/// <summary>
/// Configuration error that must abort startup instead of being swallowed by the seeder's
/// best-effort guards around the legacy data migration blocks.
/// </summary>
public sealed class BootstrapAdminConfigurationException : InvalidOperationException
{
    public BootstrapAdminConfigurationException(string message) : base(message) { }
}
