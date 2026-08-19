using System.Security.Claims;
using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Authorization;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using aspire_react.Server.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// Chống self-lockout cho API gán quyền (user↔group + group permissions).
/// Mọi test đều mô phỏng ĐÚNG tình huống: user đang thao tác TỰ gỡ quyền của CHÍNH HỌ
/// (targetUserId == actorId), hoặc Admin sửa permission của group mà chính họ là thành viên.
/// </summary>
public class PermissionLockoutTests
{
    private static AppDbContext CreateContext(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>Group có admin grant + users.edit grant.</summary>
    private static async Task<PermissionGroup> SeedAdminGroupAsync(AppDbContext db, string name = "Admins")
    {
        var group = new PermissionGroup { Name = name };
        db.PermissionGroups.Add(group);
        await db.SaveChangesAsync();
        db.GroupPermissions.Add(new GroupPermission { GroupId = group.Id, PermissionKey = "admin", Value = PermissionValue.Grant });
        db.GroupPermissions.Add(new GroupPermission { GroupId = group.Id, PermissionKey = "users.edit", Value = PermissionValue.Grant });
        await db.SaveChangesAsync();
        return group;
    }

    private static async Task<User> AddUserAsync(AppDbContext db, string username, Guid? groupId = null, bool isSuperUser = false)
    {
        var user = new User { Username = username, Email = $"{username}@local", IsSuperUser = isSuperUser };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        if (groupId.HasValue)
        {
            db.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = groupId.Value });
            await db.SaveChangesAsync();
        }
        return user;
    }

    private static ClaimsPrincipal CreatePrincipal(Guid localUserId, bool realmSuper = false, string username = "admin1")
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

    private static GroupsController CreateGroupsController(AppDbContext db, ClaimsPrincipal principal)
    {
        var httpContext = new DefaultHttpContext { User = principal };
        var controller = new GroupsController(
            db,
            new ActionLogService(db, new HttpContextAccessor { HttpContext = httpContext }),
            new PermissionLockoutGuard(db));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static UsersController CreateUsersController(AppDbContext db, ClaimsPrincipal principal)
    {
        var httpContext = new DefaultHttpContext { User = principal };
        var controller = new UsersController(
            mediator: null!,
            context: db,
            actionLogService: new ActionLogService(db, new HttpContextAccessor { HttpContext = httpContext }),
            lockoutGuard: new PermissionLockoutGuard(db),
            companyScope: new TestHelpers.SuperUserScope());
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    // =========================================================================
    // GUARD UNIT TESTS — user TỰ gỡ quyền của chính mình
    // =========================================================================

    [Fact]
    public async Task SelfRemove_LastAdminGroup_Blocked()
    {
        await using var db = CreateContext(nameof(SelfRemove_LastAdminGroup_Blocked));
        var group = await SeedAdminGroupAsync(db);
        var admin = await AddUserAsync(db, "admin1", group.Id);

        var guard = new PermissionLockoutGuard(db);
        var wouldLock = await guard.WouldSelfAssignLockoutAsync(admin.Id, admin.Id, new List<Guid>());
        Assert.True(wouldLock); // admin duy nhất tự gỡ nhóm admin → chặn
    }

    [Fact]
    public async Task SelfRemove_LastAdminGroup_Allowed_WhenAnotherAdminExists()
    {
        await using var db = CreateContext(nameof(SelfRemove_LastAdminGroup_Allowed_WhenAnotherAdminExists));
        var group = await SeedAdminGroupAsync(db);
        var adminA = await AddUserAsync(db, "adminA", group.Id);
        var adminB = await AddUserAsync(db, "adminB", group.Id);

        var guard = new PermissionLockoutGuard(db);
        var wouldLock = await guard.WouldSelfAssignLockoutAsync(adminA.Id, adminA.Id, new List<Guid>());
        Assert.False(wouldLock); // còn adminB giữ quyền → cho phép
    }

    [Fact]
    public async Task SelfRemove_OnlyUsersEditHolder_Blocked()
    {
        await using var db = CreateContext(nameof(SelfRemove_OnlyUsersEditHolder_Blocked));
        // User KHÔNG có admin (chỉ users.edit — không mở được API bảo vệ bằng policy admin).
        // Self-remove → sau thay đổi hệ thống không còn ai giữ admin → fail-closed: chặn.
        var group = new PermissionGroup { Name = "UserEditors" };
        db.PermissionGroups.Add(group);
        await db.SaveChangesAsync();
        db.GroupPermissions.Add(new GroupPermission { GroupId = group.Id, PermissionKey = "users.edit", Value = PermissionValue.Grant });
        await db.SaveChangesAsync();
        var editor = new User { Username = "editor1", Email = "editor1@local" };
        db.Users.Add(editor);
        await db.SaveChangesAsync();
        db.UserGroups.Add(new UserGroup { UserId = editor.Id, GroupId = group.Id });
        await db.SaveChangesAsync();

        var guard = new PermissionLockoutGuard(db);
        var wouldLock = await guard.WouldSelfAssignLockoutAsync(editor.Id, editor.Id, new List<Guid>());
        Assert.True(wouldLock);
    }

    [Fact]
    public async Task SelfRemove_Superuser_Allowed()
    {
        await using var db = CreateContext(nameof(SelfRemove_Superuser_Allowed));
        var superUser = await AddUserAsync(db, "super1", isSuperUser: true);

        var guard = new PermissionLockoutGuard(db);
        var wouldLock = await guard.WouldSelfAssignLockoutAsync(superUser.Id, superUser.Id, new List<Guid>());
        Assert.False(wouldLock); // superuser bypass
    }

    [Fact]
    public async Task SelfRemove_RealmSuperUser_Allowed()
    {
        await using var db = CreateContext(nameof(SelfRemove_RealmSuperUser_Allowed));
        var user = await AddUserAsync(db, "realmadmin"); // không có group admin

        var guard = new PermissionLockoutGuard(db);
        var wouldLock = await guard.WouldSelfAssignLockoutAsync(user.Id, user.Id, new List<Guid>(), actorIsRealmSuperUser: true);
        Assert.False(wouldLock); // realm role admin → bypass
    }

    [Fact]
    public async Task RemoveOtherUser_Groups_NotGuarded()
    {
        await using var db = CreateContext(nameof(RemoveOtherUser_Groups_NotGuarded));
        var group = await SeedAdminGroupAsync(db);
        var adminA = await AddUserAsync(db, "adminA", group.Id);
        var adminB = await AddUserAsync(db, "adminB", group.Id);

        var guard = new PermissionLockoutGuard(db);
        // A gỡ quyền của B (target != actor) → không guard
        var wouldLock = await guard.WouldSelfAssignLockoutAsync(adminA.Id, adminB.Id, new List<Guid>());
        Assert.False(wouldLock);
    }

    [Fact]
    public async Task SelfRemove_LastAdminGroup_ButOtherSuperUserExists_Allowed()
    {
        await using var db = CreateContext(nameof(SelfRemove_LastAdminGroup_ButOtherSuperUserExists_Allowed));
        var group = await SeedAdminGroupAsync(db);
        var admin = await AddUserAsync(db, "admin1", group.Id);
        await AddUserAsync(db, "super1", isSuperUser: true);

        var guard = new PermissionLockoutGuard(db);
        var wouldLock = await guard.WouldSelfAssignLockoutAsync(admin.Id, admin.Id, new List<Guid>());
        Assert.False(wouldLock); // còn superuser → không lockout
    }

    [Fact]
    public async Task SelfRemove_LosesAdmin_ButKeepsUsersEdit_Blocked()
    {
        await using var db = CreateContext(nameof(SelfRemove_LosesAdmin_ButKeepsUsersEdit_Blocked));
        var admins = await SeedAdminGroupAsync(db);
        var editors = new PermissionGroup { Name = "Editors" };
        db.PermissionGroups.Add(editors);
        await db.SaveChangesAsync();
        db.GroupPermissions.Add(new GroupPermission { GroupId = editors.Id, PermissionKey = "users.edit", Value = PermissionValue.Grant });
        await db.SaveChangesAsync();

        var admin = new User { Username = "admin1", Email = "admin1@local" };
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        db.UserGroups.Add(new UserGroup { UserId = admin.Id, GroupId = admins.Id });
        db.UserGroups.Add(new UserGroup { UserId = admin.Id, GroupId = editors.Id });
        await db.SaveChangesAsync();

        var guard = new PermissionLockoutGuard(db);
        // Gỡ nhóm Admins nhưng GIỮ nhóm Editors (users.edit): actor MẤT admin nhưng vẫn còn
        // users.edit. Vì users.edit KHÔNG mở lại được 2 API được guard (policy `admin`), guard
        // mới coi actor đã mất khả năng quản lý → chặn. (Guard cũ xử lý SAI ở case này.)
        var wouldLock = await guard.WouldSelfAssignLockoutAsync(admin.Id, admin.Id, new List<Guid> { editors.Id });
        Assert.True(wouldLock);
    }

    // =========================================================================
    // GROUP PERMISSION EDIT — Admin sửa permission của group chính họ đang là thành viên
    // =========================================================================

    [Fact]
    public async Task GroupEdit_RemoveAdminFromOwnLastGroup_Blocked()
    {
        await using var db = CreateContext(nameof(GroupEdit_RemoveAdminFromOwnLastGroup_Blocked));
        var group = await SeedAdminGroupAsync(db);
        var admin = await AddUserAsync(db, "admin1", group.Id);

        var guard = new PermissionLockoutGuard(db);
        // Gỡ toàn bộ permission (kể cả admin/users.edit) khỏi group duy nhất của admin → chặn
        var wouldLock = await guard.WouldGroupPermissionEditLockoutAsync(admin.Id, group.Id, new List<GroupPermissionDraft>());
        Assert.True(wouldLock);
    }

    [Fact]
    public async Task GroupEdit_RemoveAdmin_BlockedWhenOtherAdminInSameGroup()
    {
        await using var db = CreateContext(nameof(GroupEdit_RemoveAdmin_BlockedWhenOtherAdminInSameGroup));
        // A và B CÙNG thuộc "Admins" — gỡ admin khỏi group này thì CẢ HAI mất quyền,
        // hệ thống không còn admin nào → phải chặn (guard tính khả năng người khác với bộ MỚI)
        var group = await SeedAdminGroupAsync(db);
        var adminA = await AddUserAsync(db, "adminA", group.Id);
        await AddUserAsync(db, "adminB", group.Id);

        var guard = new PermissionLockoutGuard(db);
        var wouldLock = await guard.WouldGroupPermissionEditLockoutAsync(adminA.Id, group.Id, new List<GroupPermissionDraft>());
        Assert.True(wouldLock);
    }

    [Fact]
    public async Task GroupEdit_RemoveAdmin_Allowed_WhenOtherAdminInDifferentGroup()
    {
        await using var db = CreateContext(nameof(GroupEdit_RemoveAdmin_Allowed_WhenOtherAdminInDifferentGroup));
        var adminsA = await SeedAdminGroupAsync(db, "AdminsA");
        var adminsB = await SeedAdminGroupAsync(db, "AdminsB");
        var adminA = await AddUserAsync(db, "adminA", adminsA.Id);
        await AddUserAsync(db, "adminB", adminsB.Id);

        var guard = new PermissionLockoutGuard(db);
        // A gỡ admin khỏi "AdminsA" của mình, nhưng adminB (group khác) vẫn giữ quyền → cho phép
        var wouldLock = await guard.WouldGroupPermissionEditLockoutAsync(adminA.Id, adminsA.Id, new List<GroupPermissionDraft>());
        Assert.False(wouldLock);
    }

    [Fact]
    public async Task GroupEdit_KeepAdmin_Allowed()
    {
        await using var db = CreateContext(nameof(GroupEdit_KeepAdmin_Allowed));
        var group = await SeedAdminGroupAsync(db);
        var admin = await AddUserAsync(db, "admin1", group.Id);

        var guard = new PermissionLockoutGuard(db);
        // Giữ lại admin grant → vẫn còn khả năng → cho phép
        var drafts = new List<GroupPermissionDraft>
        {
            new("admin", PermissionValue.Grant),
            new("assets.view", PermissionValue.Grant)
        };
        var wouldLock = await guard.WouldGroupPermissionEditLockoutAsync(admin.Id, group.Id, drafts);
        Assert.False(wouldLock);
    }

    // =========================================================================
    // CONTROLLER TESTS — luồng thật qua ClaimsPrincipal (self-removal của chính actor)
    // =========================================================================

    [Fact]
    public async Task UpdateUserGroups_SelfRemoveLastAdmin_Returns400SelfLockout()
    {
        await using var db = CreateContext(nameof(UpdateUserGroups_SelfRemoveLastAdmin_Returns400SelfLockout));
        var group = await SeedAdminGroupAsync(db);
        var admin = await AddUserAsync(db, "admin1", group.Id);

        var principal = CreatePrincipal(admin.Id);
        var controller = CreateUsersController(db, principal);

        // Admin TỰ gỡ nhóm admin duy nhất của chính mình (không còn admin khác) → 400 SELF_LOCKOUT
        var result = await controller.UpdateUserGroups(admin.Id, new UpdateUserGroupsRequest(new List<Guid>()));
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("SELF_LOCKOUT", JsonSerializer.Serialize(bad.Value));

        // Quyền của admin KHÔNG bị thay đổi
        Assert.Equal(1, await db.UserGroups.CountAsync(ug => ug.UserId == admin.Id));
    }

    [Fact]
    public async Task UpdateUserGroups_SelfRemoveAdmin_ButKeepUsersEdit_Returns400SelfLockout()
    {
        await using var db = CreateContext(nameof(UpdateUserGroups_SelfRemoveAdmin_ButKeepUsersEdit_Returns400SelfLockout));
        var admins = await SeedAdminGroupAsync(db);
        var editors = new PermissionGroup { Name = "Editors" };
        db.PermissionGroups.Add(editors);
        await db.SaveChangesAsync();
        db.GroupPermissions.Add(new GroupPermission { GroupId = editors.Id, PermissionKey = "users.edit", Value = PermissionValue.Grant });
        await db.SaveChangesAsync();

        var admin = new User { Username = "admin1", Email = "admin1@local" };
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        db.UserGroups.Add(new UserGroup { UserId = admin.Id, GroupId = admins.Id });
        db.UserGroups.Add(new UserGroup { UserId = admin.Id, GroupId = editors.Id });
        await db.SaveChangesAsync();

        var principal = CreatePrincipal(admin.Id);
        var controller = CreateUsersController(db, principal);

        // Admin TỰ gỡ nhóm admin nhưng GIỮ nhóm chỉ có users.edit → MẤT khả năng gọi lại API
        // (policy `admin`) → vẫn phải chặn SELF_LOCKOUT. (Guard cũ cho phép — là lỗ hổng.)
        var result = await controller.UpdateUserGroups(admin.Id, new UpdateUserGroupsRequest(new List<Guid> { editors.Id }));
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("SELF_LOCKOUT", JsonSerializer.Serialize(bad.Value));

        // Quyền của admin KHÔNG bị thay đổi
        Assert.Equal(2, await db.UserGroups.CountAsync(ug => ug.UserId == admin.Id));
    }

    [Fact]
    public async Task UpdateUserGroups_SelfRemove_Allowed_WhenAnotherAdminExists()
    {
        await using var db = CreateContext(nameof(UpdateUserGroups_SelfRemove_Allowed_WhenAnotherAdminExists));
        var group = await SeedAdminGroupAsync(db);
        var adminA = await AddUserAsync(db, "adminA", group.Id);
        await AddUserAsync(db, "adminB", group.Id);

        var principal = CreatePrincipal(adminA.Id);
        var controller = CreateUsersController(db, principal);

        var result = await controller.UpdateUserGroups(adminA.Id, new UpdateUserGroupsRequest(new List<Guid>()));
        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(0, await db.UserGroups.CountAsync(ug => ug.UserId == adminA.Id));
        Assert.Equal(1, await db.UserGroups.CountAsync(ug => ug.UserId != adminA.Id));
    }

    [Fact]
    public async Task UpdateUserGroups_SelfRemove_Superuser_Returns200()
    {
        await using var db = CreateContext(nameof(UpdateUserGroups_SelfRemove_Superuser_Returns200));
        var superUser = await AddUserAsync(db, "super1", isSuperUser: true);

        var principal = CreatePrincipal(superUser.Id);
        var controller = CreateUsersController(db, principal);

        var result = await controller.UpdateUserGroups(superUser.Id, new UpdateUserGroupsRequest(new List<Guid>()));
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task UpdateUserGroups_SelfRemove_RealmSuperUser_Returns200()
    {
        await using var db = CreateContext(nameof(UpdateUserGroups_SelfRemove_RealmSuperUser_Returns200));
        var user = await AddUserAsync(db, "realmadmin");

        // realm role admin → bypass guard (dù không có group admin nào)
        var principal = CreatePrincipal(user.Id, realmSuper: true);
        var controller = CreateUsersController(db, principal);

        var result = await controller.UpdateUserGroups(user.Id, new UpdateUserGroupsRequest(new List<Guid>()));
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task UpdateUserGroups_InvalidGroup_Returns400GroupNotFound()
    {
        await using var db = CreateContext(nameof(UpdateUserGroups_InvalidGroup_Returns400GroupNotFound));
        var group = await SeedAdminGroupAsync(db);
        var admin = await AddUserAsync(db, "admin1", group.Id);

        var principal = CreatePrincipal(admin.Id);
        var controller = CreateUsersController(db, principal);

        var result = await controller.UpdateUserGroups(admin.Id, new UpdateUserGroupsRequest(new List<Guid> { Guid.NewGuid() }));
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("GROUP_NOT_FOUND", JsonSerializer.Serialize(bad.Value));
    }

    [Fact]
    public async Task UpdateUserGroups_AssignOtherUser_LogsActionLog()
    {
        await using var db = CreateContext(nameof(UpdateUserGroups_AssignOtherUser_LogsActionLog));
        var group = await SeedAdminGroupAsync(db);
        var adminA = await AddUserAsync(db, "adminA", group.Id);
        var targetUser = await AddUserAsync(db, "targetUser");

        var principal = CreatePrincipal(adminA.Id);
        var controller = CreateUsersController(db, principal);

        var result = await controller.UpdateUserGroups(targetUser.Id, new UpdateUserGroupsRequest(new List<Guid> { group.Id }));
        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, await db.UserGroups.CountAsync(ug => ug.UserId == targetUser.Id));

        // Hành động nhạy cảm phải được ghi ActionLog (ItemType.User, Update)
        var log = await db.ActionLogs.SingleOrDefaultAsync(l =>
            l.ItemType == ItemType.User && l.ItemId == targetUser.Id && l.ActionType == ActionType.Update);
        Assert.NotNull(log);
        Assert.Equal(adminA.Id, log.CreatedBy);
        Assert.Contains("groupIds", log.LogMeta);
    }

    [Fact]
    public async Task UpdateGroupPermissions_SelfLockout_Returns400()
    {
        await using var db = CreateContext(nameof(UpdateGroupPermissions_SelfLockout_Returns400));
        var group = await SeedAdminGroupAsync(db);
        var admin = await AddUserAsync(db, "admin1", group.Id);

        var principal = CreatePrincipal(admin.Id);
        var controller = CreateGroupsController(db, principal);

        // Gỡ toàn bộ permission (kể cả admin) khỏi group duy nhất của admin → 400 SELF_LOCKOUT
        var result = await controller.UpdateGroupPermissions(group.Id, new List<PermissionEntry>());
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("SELF_LOCKOUT", JsonSerializer.Serialize(bad.Value));
        Assert.Equal(2, await db.GroupPermissions.CountAsync(gp => gp.GroupId == group.Id));
    }

    [Fact]
    public async Task CreateGroup_LogsActionLog()
    {
        await using var db = CreateContext(nameof(CreateGroup_LogsActionLog));
        var admin = await AddUserAsync(db, "admin1");

        var principal = CreatePrincipal(admin.Id);
        var controller = CreateGroupsController(db, principal);

        var result = await controller.CreateGroup(new CreateGroupRequest("New Group", null));
        Assert.IsType<CreatedAtActionResult>(result);

        var log = await db.ActionLogs.SingleOrDefaultAsync(l =>
            l.ItemType == ItemType.PermissionGroup && l.ActionType == ActionType.Create);
        Assert.NotNull(log);
        Assert.Equal(admin.Id, log.CreatedBy);
    }
}
