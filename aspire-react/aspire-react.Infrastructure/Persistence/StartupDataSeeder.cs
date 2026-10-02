using aspire_react.Server.Domain.Authorization;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace aspire_react.Server.Infrastructure.Persistence;

/// <summary>
/// Startup data/migration seeding — replaces the inline block that used to live in Program.cs
/// (and the previously-deleted DbInitializer). Runs synchronously at startup (after Build, before
/// Run) via a single <c>StartupDataSeeder.Seed(services)</c> call. Idempotent — safe on every boot.
/// </summary>
public static class StartupDataSeeder
{
    public static void Seed(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Auto-create/upgrade database schema on startup (EF Core migrations).
        db.Database.Migrate();

        // === Seed default system groups (Superuser / Admin) — only when no system group exists. ===
        // No user is auto-assigned here, so this does NOT change any existing user's permissions.
        // Assigning users to groups is a separate (Subtask B/E) migration step.
        try
        {
            var hasSystemGroup = db.PermissionGroups.Any(g => g.IsSystem);
            if (!hasSystemGroup)
            {
                var superuserGroup = db.PermissionGroups.FirstOrDefault(g => g.Name == "Superuser");
                if (superuserGroup == null)
                {
                    superuserGroup = new PermissionGroup
                    {
                        Name = "Superuser",
                        Description = "Toàn quyền hệ thống — nhóm hệ thống, không thể xóa/đổi tên.",
                        IsSystem = true
                    };
                    db.PermissionGroups.Add(superuserGroup);
                }
                else
                {
                    superuserGroup.IsSystem = true;
                }

                var adminGroup = db.PermissionGroups.FirstOrDefault(g => g.Name == "Admin");
                if (adminGroup == null)
                {
                    adminGroup = new PermissionGroup
                    {
                        Name = "Admin",
                        Description = "Quản trị viên — nhóm hệ thống, không thể xóa/đổi tên.",
                        IsSystem = true
                    };
                    db.PermissionGroups.Add(adminGroup);
                }
                else
                {
                    adminGroup.IsSystem = true;
                }

                db.SaveChanges();

                foreach (var permission in PermissionCatalog.All)
                {
                    if (!db.GroupPermissions.Any(gp => gp.GroupId == superuserGroup.Id && gp.PermissionKey == permission.Code))
                    {
                        db.GroupPermissions.Add(new GroupPermission
                        {
                            GroupId = superuserGroup.Id,
                            PermissionKey = permission.Code,
                            Value = PermissionValue.Grant
                        });
                    }

                    if (!db.GroupPermissions.Any(gp => gp.GroupId == adminGroup.Id && gp.PermissionKey == permission.Code))
                    {
                        db.GroupPermissions.Add(new GroupPermission
                        {
                            GroupId = adminGroup.Id,
                            PermissionKey = permission.Code,
                            Value = PermissionValue.Grant
                        });
                    }
                }

                db.SaveChanges();
            }
        }
        catch { }

        // === v7: Migration dữ liệu cũ → nhóm — gán user legacy IsSuperUser vào nhóm "Superuser".
        // Chỉ THÊM membership, idempotent → không bao giờ thu hẹp quyền hiện có (xem PermissionMigration). ===
        try { PermissionMigration.AssignLegacySuperUsersToSuperuserGroupAsync(db).GetAwaiter().GetResult(); } catch { }

        // [FIX-DEPLOY 2026-10-02] Bootstrap admin USER + local password.
        // Local auth replaced Keycloak (AUTH Phase 5 removed the Keycloak seed path and JIT
        // provisioning), so on a FRESH database nothing else creates the first administrator —
        // without this the deployment comes up but nobody can log in. Behaviour:
        //   1. If no user with INITIAL_ADMIN_USERNAME (default "admin") exists AND the configured
        //      email is free AND Auth:BootstrapAdminPassword is set → create the superuser.
        //   2. Otherwise, if that admin exists without a local PasswordHash → seed the hash.
        // Idempotent: an existing user/hash is NEVER overwritten here.
        try
        {
            var config = services.GetRequiredService<IConfiguration>();
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("StartupDataSeeder");
            var bootstrapPassword = config["Auth:BootstrapAdminPassword"];
            if (!string.IsNullOrEmpty(bootstrapPassword))
            {
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
                    }
                    else
                    {
                        db.Users.Add(new User
                        {
                            Username = adminUsername,
                            Email = adminEmail,
                            FirstName = "System",
                            LastName = "Admin",
                            PasswordHash = new Authentication.PasswordHasherService().Hash(bootstrapPassword),
                            MustChangePassword = false, // bootstrap admin is trusted; no forced change
                            IsSuperUser = true,
                            IsActive = true
                        });
                        db.SaveChanges();
                        logger.LogInformation("Bootstrap admin '{Username}' created (local auth).", adminUsername);
                    }
                }
                else if (string.IsNullOrEmpty(admin.PasswordHash))
                {
                    admin.PasswordHash = new Authentication.PasswordHasherService().Hash(bootstrapPassword);
                    admin.MustChangePassword = false; // bootstrap admin is trusted; no forced change
                    db.SaveChanges();
                    logger.LogInformation("Bootstrap password seeded for existing admin '{Username}'.", adminUsername);
                }
            }
        }
        catch { }
    }
}
