using System.Security.Claims;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Authorization;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// Migration dữ liệu cũ → nhóm (Subtask E): map user legacy (<c>IsSuperUser = true</c>) vào
/// nhóm hệ thống "Superuser". Regression QUAN TRỌNG NHẤT: KHÔNG thu hẹp quyền hiện có —
/// user admin cũ vẫn thao tác đúng những gì họ làm được trước đây.
/// </summary>
public class PermissionMigrationTests
{
    private static AppDbContext CreateContext(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<PermissionGroup> SeedSuperuserGroupAsync(AppDbContext db)
    {
        var group = new PermissionGroup { Name = "Superuser", IsSystem = true };
        db.PermissionGroups.Add(group);
        await db.SaveChangesAsync();
        foreach (var p in PermissionCatalog.All)
        {
            db.GroupPermissions.Add(new GroupPermission { GroupId = group.Id, PermissionKey = p.Code, Value = PermissionValue.Grant });
        }
        await db.SaveChangesAsync();
        return group;
    }

    private static ClaimsPrincipal CreatePrincipal(Guid localUserId, bool realmSuper = false, string username = "user1")
    {
        var claims = new List<Claim>
        {
            new("preferred_username", username),
            new(ClaimTypes.NameIdentifier, "kc-sub-" + username),
            new("local_user_id", localUserId.ToString()),
            new("realm_access", realmSuper ? "{\"roles\":[\"admin\"]}" : "{\"roles\":[\"user\"]}")
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static async Task<bool> EvaluateAsync(AppDbContext db, ClaimsPrincipal principal, string permissionKey)
    {
        var requirement = new PermissionRequirement(permissionKey);
        var authContext = new AuthorizationHandlerContext(
            new IAuthorizationRequirement[] { requirement }, principal, resource: null);
        var handler = new PermissionHandler(db, new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = principal }
        });
        await handler.HandleAsync(authContext);
        return authContext.HasSucceeded;
    }

    // =========================================================================
    // 1. Migration gán user legacy vào nhóm Superuser
    // =========================================================================

    [Fact]
    public async Task LegacySuperUser_NoGroup_AssignedToSuperuserGroup()
    {
        await using var db = CreateContext(nameof(LegacySuperUser_NoGroup_AssignedToSuperuserGroup));
        var group = await SeedSuperuserGroupAsync(db);
        var user = new User { Username = "legacyadmin", Email = "legacy@local", IsSuperUser = true };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await PermissionMigration.AssignLegacySuperUsersToSuperuserGroupAsync(db);

        Assert.True(await db.UserGroups.AnyAsync(ug => ug.UserId == user.Id && ug.GroupId == group.Id));
    }

    [Fact]
    public async Task LegacySuperUser_AlreadyAssigned_NotDuplicated()
    {
        await using var db = CreateContext(nameof(LegacySuperUser_AlreadyAssigned_NotDuplicated));
        var group = await SeedSuperuserGroupAsync(db);
        var user = new User { Username = "legacyadmin", Email = "legacy@local", IsSuperUser = true };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = group.Id });
        await db.SaveChangesAsync();

        // Chạy nhiều lần — không tạo trùng lặp (idempotent).
        await PermissionMigration.AssignLegacySuperUsersToSuperuserGroupAsync(db);
        await PermissionMigration.AssignLegacySuperUsersToSuperuserGroupAsync(db);

        Assert.Equal(1, await db.UserGroups.CountAsync(ug => ug.UserId == user.Id));
    }

    [Fact]
    public async Task RegularUser_NotAssigned()
    {
        await using var db = CreateContext(nameof(RegularUser_NotAssigned));
        await SeedSuperuserGroupAsync(db);
        var user = new User { Username = "regular", Email = "regular@local", IsSuperUser = false };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await PermissionMigration.AssignLegacySuperUsersToSuperuserGroupAsync(db);

        Assert.Equal(0, await db.UserGroups.CountAsync(ug => ug.UserId == user.Id));
    }

    [Fact]
    public async Task Migration_DoesNotTouchExistingPermissions()
    {
        await using var db = CreateContext(nameof(Migration_DoesNotTouchExistingPermissions));
        var group = await SeedSuperuserGroupAsync(db);
        var user = new User { Username = "legacyadmin", Email = "legacy@local", IsSuperUser = true };
        db.Users.Add(user);
        db.UserPermissions.Add(new UserPermission { UserId = user.Id, PermissionKey = "assets.view", Value = PermissionValue.Grant });
        await db.SaveChangesAsync();

        await PermissionMigration.AssignLegacySuperUsersToSuperuserGroupAsync(db);

        // Chỉ thêm UserGroup — UserPermission và GroupPermission hiện có không đổi.
        Assert.Equal(1, await db.UserPermissions.CountAsync());
        Assert.Equal(PermissionCatalog.All.Count, await db.GroupPermissions.CountAsync(gp => gp.GroupId == group.Id));
        Assert.Equal(1, await db.UserGroups.CountAsync(ug => ug.UserId == user.Id));
    }

    // =========================================================================
    // 2. Regression QUAN TRỌNG NHẤT — không thu hẹp quyền hiện có
    // =========================================================================

    [Fact]
    public async Task LegacySuperUser_AfterMigration_StillHasFullAccess()
    {
        await using var db = CreateContext(nameof(LegacySuperUser_AfterMigration_StillHasFullAccess));
        var group = await SeedSuperuserGroupAsync(db);
        // Mô phỏng migration thật: user legacy được gán vào nhóm Superuser (như migration làm).
        var user = new User { Username = "legacyadmin", Email = "legacy@local", IsSuperUser = true };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await PermissionMigration.AssignLegacySuperUsersToSuperuserGroupAsync(db);

        var principal = CreatePrincipal(user.Id);

        // Admin cũ vẫn thao tác được đúng những gì họ làm được trước đây — qua flag (step 3)
        // và qua nhóm Superuser (step 5) — 2 lớp an toàn, không mất quyền nào.
        foreach (var code in new[] { "assets.view", "assets.delete", "licenses.create", "users.edit", "customfields.delete", "import", "reports.view", "admin", "superuser" })
        {
            Assert.True(await EvaluateAsync(db, principal, code), $"Legacy superuser phải còn quyền: {code}");
        }
    }

    [Fact]
    public async Task RegularUser_AccessUnchangedByMigration()
    {
        await using var db = CreateContext(nameof(RegularUser_AccessUnchangedByMigration));
        var group = await SeedSuperuserGroupAsync(db);
        // User thường chỉ có assets.view (direct grant) — migration không gán vào Superuser,
        // quyền của họ không đổi.
        var user = new User { Username = "viewer", Email = "viewer@local" };
        db.Users.Add(user);
        db.UserPermissions.Add(new UserPermission { UserId = user.Id, PermissionKey = "assets.view", Value = PermissionValue.Grant });
        await db.SaveChangesAsync();

        await PermissionMigration.AssignLegacySuperUsersToSuperuserGroupAsync(db);

        var principal = CreatePrincipal(user.Id);
        Assert.True(await EvaluateAsync(db, principal, "assets.view"));
        Assert.False(await EvaluateAsync(db, principal, "assets.edit"));
        Assert.False(await EvaluateAsync(db, principal, "licenses.create"));
    }

    // =========================================================================
    // 3. Test tổng hợp (integration) — hệ thống đã seed + gán nhóm hoạt động đúng
    // =========================================================================

    [Fact]
    public async Task EndToEnd_SeededSystem_AuthorizationWorks()
    {
        await using var db = CreateContext(nameof(EndToEnd_SeededSystem_AuthorizationWorks));
        // Mô phỏng startup seed (Subtask A): Superuser + Admin với đủ 76 permission.
        var superuserGroup = await SeedSuperuserGroupAsync(db);
        var adminGroup = new PermissionGroup { Name = "Admin", IsSystem = true };
        db.PermissionGroups.Add(adminGroup);
        await db.SaveChangesAsync();
        foreach (var p in PermissionCatalog.All)
        {
            db.GroupPermissions.Add(new GroupPermission { GroupId = adminGroup.Id, PermissionKey = p.Code, Value = PermissionValue.Grant });
        }
        var viewerGroup = new PermissionGroup { Name = "Viewer" };
        db.PermissionGroups.Add(viewerGroup);
        await db.SaveChangesAsync();
        db.GroupPermissions.Add(new GroupPermission { GroupId = viewerGroup.Id, PermissionKey = "assets.view", Value = PermissionValue.Grant });

        var admin = new User { Username = "admin", Email = "admin@local" };
        var viewer = new User { Username = "viewer", Email = "viewer@local" };
        var nobody = new User { Username = "nobody", Email = "nobody@local" };
        db.Users.AddRange(admin, viewer, nobody);
        await db.SaveChangesAsync();
        db.UserGroups.Add(new UserGroup { UserId = admin.Id, GroupId = superuserGroup.Id });
        db.UserGroups.Add(new UserGroup { UserId = viewer.Id, GroupId = viewerGroup.Id });
        await db.SaveChangesAsync();

        var adminPrincipal = CreatePrincipal(admin.Id);
        var viewerPrincipal = CreatePrincipal(viewer.Id);
        var nobodyPrincipal = CreatePrincipal(nobody.Id);

        // Admin (Superuser group) → đủ quyền đại diện mọi module.
        foreach (var code in new[] { "assets.view", "assets.delete", "consumables.checkout", "components.edit", "accessories.delete", "licenses.view", "users.delete", "companies.create", "models.edit", "categories.delete", "manufacturers.view", "suppliers.edit", "departments.view", "locations.delete", "statuslabels.view", "reports.view", "customfields.delete", "import", "export", "admin" })
        {
            Assert.True(await EvaluateAsync(db, adminPrincipal, code), $"Admin phải có quyền: {code}");
        }

        // Viewer (chỉ assets.view) → đúng, không hơn không kém.
        Assert.True(await EvaluateAsync(db, viewerPrincipal, "assets.view"));
        Assert.False(await EvaluateAsync(db, viewerPrincipal, "assets.edit"));
        Assert.False(await EvaluateAsync(db, viewerPrincipal, "users.view"));
        Assert.False(await EvaluateAsync(db, viewerPrincipal, "admin"));

        // Nobody (không nhóm) → default deny.
        Assert.False(await EvaluateAsync(db, nobodyPrincipal, "assets.view"));
        Assert.False(await EvaluateAsync(db, nobodyPrincipal, "users.view"));
    }
}
