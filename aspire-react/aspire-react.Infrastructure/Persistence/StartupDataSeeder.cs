using System.Text.Json;
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

        // [READY GĐ0] Bootstrap policy lives in BootstrapAdminSeeder: on a blank DB the one-time
        // password must be >= 12 characters and different from the username, otherwise startup
        // aborts. Existing admins are never re-validated or overwritten.
        try
        {
            var config = services.GetRequiredService<IConfiguration>();
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("StartupDataSeeder");
            BootstrapAdminSeeder.Ensure(db, config, logger);
            MigrateDf7TicketPermissions(db, logger);
        }
        catch (BootstrapAdminConfigurationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("StartupDataSeeder");
            logger.LogError(ex, "DF-7 ticket-permission migration failed; it will retry on next startup.");
        }
    }

    // [DF-7] One-time data migration. The SystemSetting flag is the source of truth: once written,
    // later boots do nothing even if an admin removes a granted key from a group.
    public const string Df7TicketPermissionsFlag = "migration.df7_ticket_permissions";
    private static readonly string[] Df7TicketPermissionKeys =
    {
        "maintenance.tickets",
        "maintenance.tickets.approve",
    };

    public static void MigrateDf7TicketPermissions(AppDbContext db, ILogger? logger = null)
    {
        if (db.SystemSettings.Any(s => s.Key == Df7TicketPermissionsFlag))
            return;

        // ActionLog.CreatedBy is a real FK. Startup bootstrap creates/keeps the admin account before
        // this migration; if there is still no user, do not consume the one-shot flag.
        var actorId = db.Users.Where(u => u.IsSuperUser).Select(u => u.Id).FirstOrDefault();
        if (actorId == Guid.Empty)
            actorId = db.Users.Select(u => u.Id).FirstOrDefault();
        if (actorId == Guid.Empty)
            return;

        var groupIds = db.GroupPermissions
            .Where(gp => gp.PermissionKey == "assets.edit" && gp.Value == PermissionValue.Grant)
            .Select(gp => gp.GroupId)
            .Distinct()
            .ToList();

        // A brand-new database has only the system Superuser/Admin groups at this point; demo /
        // business groups are created later through their own flows. Defer the one-shot flag until
        // at least one non-system group exists, so the migration can actually grant to it.
        if (!groupIds.Any(groupId => db.PermissionGroups.Any(g => g.Id == groupId && !g.IsSystem)))
        {
            logger?.LogInformation("DF-7 ticket-permission migration deferred: no non-system group yet.");
            return;
        }

        // One SaveChanges below commits flag + grants + audit rows in a single database transaction
        // (Npgsql itself wraps SaveChanges), so no user-initiated transaction is needed here.
        var now = DateTime.UtcNow;
        var pendingLogs = new List<ActionLog>();
        foreach (var groupId in groupIds)
        {
            foreach (var key in Df7TicketPermissionKeys)
            {
                if (db.GroupPermissions.Any(gp => gp.GroupId == groupId && gp.PermissionKey == key))
                    continue;

                db.GroupPermissions.Add(new GroupPermission
                {
                    GroupId = groupId,
                    PermissionKey = key,
                    Value = PermissionValue.Grant,
                });

                pendingLogs.Add(new ActionLog
                {
                    Id = Guid.NewGuid(),
                    ItemType = ItemType.PermissionGroup,
                    ItemId = groupId,
                    ActionType = ActionType.Update,
                    CreatedBy = actorId,
                    Note = $"DF-7 migration: cấp quyền {key} cho nhóm có assets.edit.",
                    LogMeta = JsonSerializer.Serialize(new
                    {
                        changes = new
                        {
                            permissionKey = new { old = (string?)null, @new = key },
                        },
                    }),
                    ActionDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
                    CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
                    ActionSource = ActionSource.Cli,
                });
            }
        }

        db.SystemSettings.Add(new SystemSetting
        {
            Key = Df7TicketPermissionsFlag,
            Value = "1",
            Description = "DF-7 one-time migration: grant maintenance.tickets/.approve to groups with assets.edit.",
            CreatedAt = now,
            UpdatedAt = now,
        });

        db.ActionLogs.AddRange(pendingLogs);
        db.SaveChanges();
        logger?.LogInformation(
            "DF-7 one-time migration applied: {GroupCount} groups, {GrantCount} grants.",
            groupIds.Count, pendingLogs.Count);
    }
}
