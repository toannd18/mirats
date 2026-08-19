using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Authorization;
using Microsoft.EntityFrameworkCore;

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
    }
}
