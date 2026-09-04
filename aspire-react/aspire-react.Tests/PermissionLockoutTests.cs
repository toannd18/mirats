using aspire_react.Server.Application.Common.Behaviors;
using aspire_react.Server.Application.Groups.Commands;
using aspire_react.Server.Application.Users.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Authorization;
using aspire_react.Server.Infrastructure.Persistence;
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
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
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

    // [Giai đoạn 3] Users UpdateUserGroups migrated to MediatR — the controller-level tests below
    // now drive UpdateUserGroupsCommandHandler directly (real PermissionLockoutGuard +
    // SuperUserScope; the realm-superuser flag is passed explicitly since handlers cannot read
    // HttpContext). Controller CreateUsersController helper removed (no caller left).
    private static UpdateUserGroupsCommandHandler CreateUpdateGroupsHandler(AppDbContext db)
        => new(db, new TestHelpers.SuperUserScope(), new PermissionLockoutGuard(db));

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

        // Admin TỰ gỡ nhóm admin duy nhất của chính mình (không còn admin khác) → SELF_LOCKOUT
        var handler = CreateUpdateGroupsHandler(db);
        var result = await handler.Handle(
            new UpdateUserGroupsCommand(admin.Id, new List<Guid>(), admin.Id, ActorIsRealmSuperUser: false),
            CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("SELF_LOCKOUT", result.ErrorCode);

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

        // Admin TỰ gỡ nhóm admin nhưng GIỮ nhóm chỉ có users.edit → MẤT khả năng gọi lại API
        // (policy `admin`) → vẫn phải chặn SELF_LOCKOUT. (Guard cũ cho phép — là lỗ hổng.)
        var handler = CreateUpdateGroupsHandler(db);
        var result = await handler.Handle(
            new UpdateUserGroupsCommand(admin.Id, new List<Guid> { editors.Id }, admin.Id, ActorIsRealmSuperUser: false),
            CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("SELF_LOCKOUT", result.ErrorCode);

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

        var handler = CreateUpdateGroupsHandler(db);
        var result = await handler.Handle(
            new UpdateUserGroupsCommand(adminA.Id, new List<Guid>(), adminA.Id, ActorIsRealmSuperUser: false),
            CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal(0, await db.UserGroups.CountAsync(ug => ug.UserId == adminA.Id));
        Assert.Equal(1, await db.UserGroups.CountAsync(ug => ug.UserId != adminA.Id));
    }

    [Fact]
    public async Task UpdateUserGroups_SelfRemove_Superuser_Returns200()
    {
        await using var db = CreateContext(nameof(UpdateUserGroups_SelfRemove_Superuser_Returns200));
        var superUser = await AddUserAsync(db, "super1", isSuperUser: true);

        var handler = CreateUpdateGroupsHandler(db);
        var result = await handler.Handle(
            new UpdateUserGroupsCommand(superUser.Id, new List<Guid>(), superUser.Id, ActorIsRealmSuperUser: false),
            CancellationToken.None);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task UpdateUserGroups_SelfRemove_RealmSuperUser_Returns200()
    {
        await using var db = CreateContext(nameof(UpdateUserGroups_SelfRemove_RealmSuperUser_Returns200));
        var user = await AddUserAsync(db, "realmadmin");

        // realm role admin → bypass guard (dù không có group admin nào)
        var handler = CreateUpdateGroupsHandler(db);
        var result = await handler.Handle(
            new UpdateUserGroupsCommand(user.Id, new List<Guid>(), user.Id, ActorIsRealmSuperUser: true),
            CancellationToken.None);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task UpdateUserGroups_InvalidGroup_Returns400GroupNotFound()
    {
        await using var db = CreateContext(nameof(UpdateUserGroups_InvalidGroup_Returns400GroupNotFound));
        var group = await SeedAdminGroupAsync(db);
        var admin = await AddUserAsync(db, "admin1", group.Id);

        var handler = CreateUpdateGroupsHandler(db);
        var result = await handler.Handle(
            new UpdateUserGroupsCommand(admin.Id, new List<Guid> { Guid.NewGuid() }, admin.Id, ActorIsRealmSuperUser: false),
            CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("GROUP_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task UpdateUserGroups_AssignOtherUser_LogsActionLog()
    {
        await using var db = CreateContext(nameof(UpdateUserGroups_AssignOtherUser_LogsActionLog));
        var group = await SeedAdminGroupAsync(db);
        var adminA = await AddUserAsync(db, "adminA", group.Id);
        var targetUser = await AddUserAsync(db, "targetUser");

        // [Giai đoạn 3] Drive through the REAL ActionLogBehavior chain (log written by behavior,
        // enriched — RemoteIp/UserAgent/ActionSource added per playbook §4 enrichment 2a).
        var handler = CreateUpdateGroupsHandler(db);
        var behavior = new ActionLogBehavior<UpdateUserGroupsCommand, UpdateUserGroupsResult>(
            TestHelpers.CreateActionLogService(db, adminA.Id), db);
        var cmd = new UpdateUserGroupsCommand(targetUser.Id, new List<Guid> { group.Id }, adminA.Id, ActorIsRealmSuperUser: false);
        var result = await behavior.Handle(cmd, ct => handler.Handle(cmd, ct), CancellationToken.None);
        Assert.True(result.Success);
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

        // [Giai đoạn 3] Drive UpdateGroupPermissionsCommandHandler directly — REAL guard wired in,
        // non-realm-superuser actor (the self-lockout path cannot trigger for realm superusers).
        var handler = new UpdateGroupPermissionsCommandHandler(db, new PermissionLockoutGuard(db));
        var result = await handler.Handle(
            new UpdateGroupPermissionsCommand(group.Id, new List<GroupPermissionEntry>(), admin.Id, ActorIsRealmSuperUser: false),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("SELF_LOCKOUT", result.ErrorCode);
        Assert.Equal(2, await db.GroupPermissions.CountAsync(gp => gp.GroupId == group.Id));
    }

    [Fact]
    public async Task CreateGroup_LogsActionLog()
    {
        await using var db = CreateContext(nameof(CreateGroup_LogsActionLog));
        var admin = await AddUserAsync(db, "admin1");

        // [Giai đoạn 3] Drive through the REAL ActionLogBehavior chain (log written by behavior).
        var handler = new CreateGroupCommandHandler(db);
        var behavior = new aspire_react.Server.Application.Common.Behaviors.ActionLogBehavior<
            CreateGroupCommand, GroupResult>(TestHelpers.CreateActionLogService(db, admin.Id), db);
        var cmd = new CreateGroupCommand("New Group", null, admin.Id);
        var result = await behavior.Handle(cmd, ct => handler.Handle(cmd, ct), CancellationToken.None);

        Assert.True(result.Success);

        var log = await db.ActionLogs.SingleOrDefaultAsync(l =>
            l.ItemType == ItemType.PermissionGroup && l.ActionType == ActionType.Create);
        Assert.NotNull(log);
        Assert.Equal(admin.Id, log.CreatedBy);
    }
}
