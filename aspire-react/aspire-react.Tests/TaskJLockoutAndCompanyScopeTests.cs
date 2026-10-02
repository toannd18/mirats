using System.Security.Claims;
using System.Text.Json;
using aspire_react.Server.Application.Groups.Commands;
using aspire_react.Server.Application.Users.Commands;
using aspire_react.Server.Application.Users.Validators;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Authorization;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using aspire_react.Server.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// Task J — đóng 3 đường bypass PermissionLockoutGuard (DeleteGroup, UpdateUser toggle IsSuperUser,
/// DeleteUser soft-deactivate) + bổ sung company-scoping cho UsersController.UpdateUser/DeleteUser.
/// Mỗi guard được verify 2 chiều: chặn đúng tình huống lockout thật, KHÔNG chặn khi còn quản trị khác.
/// </summary>
public class TaskJLockoutAndCompanyScopeTests
{
    private static AppDbContext CreateContext(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<PermissionGroup> SeedAdminGroupAsync(AppDbContext db, string name = "Admins")
    {
        var group = new PermissionGroup { Name = name };
        db.PermissionGroups.Add(group);
        await db.SaveChangesAsync();
        db.GroupPermissions.Add(new GroupPermission { GroupId = group.Id, PermissionKey = "admin", Value = PermissionValue.Grant });
        await db.SaveChangesAsync();
        return group;
    }

    private static async Task<Guid> AddCompanyAsync(AppDbContext db, string name)
    {
        var company = new Company { Name = name };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return company.Id;
    }

    private static async Task<User> AddUserAsync(
        AppDbContext db,
        string username,
        Guid? companyId = null,
        Guid? groupId = null,
        bool isSuperUser = false)
    {
        var user = new User
        {
            Username = username,
            Email = $"{username}@local",
            FirstName = "First",
            LastName = "Last",
            IsSuperUser = isSuperUser,
            CompanyId = companyId
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        if (groupId.HasValue)
        {
            db.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = groupId.Value });
            await db.SaveChangesAsync();
        }
        return user;
    }

    private static ClaimsPrincipal CreatePrincipal(Guid localUserId, bool realmSuper = false)
    {
        var claims = new List<Claim>
        {
            new("preferred_username", "actor"),
            new(ClaimTypes.NameIdentifier, "kc-sub-actor"),
            new("local_user_id", localUserId.ToString()),
            new("realm_access", realmSuper ? "{\"roles\":[\"admin\"]}" : "{\"roles\":[\"user\"]}")
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    // [Giai đoạn 3] Groups migrated to MediatR — the two controller-level DeleteGroup tests below
    // now drive DeleteGroupCommandHandler directly (real PermissionLockoutGuard wired in).
    // [FIX BUG-M 2026-10-02] BuildUsersController removed together with the hybrid UsersController
    // constructor: the user guards are tested at command-handler level (see BuildUpdateHandler /
    // BuildDeleteHandler below), the controller is now a thin IMediator map.

    private static UpdateUserCommand ValidUpdate(Guid id, bool isSuperUser)
        => new()
        {
            Id = id,
            FirstName = "First",
            LastName = "Last",
            Email = "unique@t.local",
            IsSuperUser = isSuperUser,
            IsActive = true,
            CompanyId = null,
            DepartmentId = null,
            LocationId = null
        };

    // =========================================================================
    // GUARD UNIT TESTS — WouldDeleteGroupLockoutAsync
    // =========================================================================

    [Fact]
    public async Task DeleteGroup_LastAdminGroup_Blocked()
    {
        await using var db = CreateContext(nameof(DeleteGroup_LastAdminGroup_Blocked));
        var group = await SeedAdminGroupAsync(db);
        var admin = await AddUserAsync(db, "admin1", groupId: group.Id);

        var guard = new PermissionLockoutGuard(db);
        Assert.True(await guard.WouldDeleteGroupLockoutAsync(admin.Id, group.Id));
    }

    [Fact]
    public async Task DeleteGroup_Allowed_WhenAnotherAdminInDifferentGroup()
    {
        await using var db = CreateContext(nameof(DeleteGroup_Allowed_WhenAnotherAdminInDifferentGroup));
        var groupA = await SeedAdminGroupAsync(db, "AdminsA");
        var groupB = await SeedAdminGroupAsync(db, "AdminsB");
        var adminA = await AddUserAsync(db, "adminA", groupId: groupA.Id);
        await AddUserAsync(db, "adminB", groupId: groupB.Id);

        var guard = new PermissionLockoutGuard(db);
        Assert.False(await guard.WouldDeleteGroupLockoutAsync(adminA.Id, groupA.Id));
    }

    [Fact]
    public async Task DeleteGroup_Allowed_WhenSuperUserExists()
    {
        await using var db = CreateContext(nameof(DeleteGroup_Allowed_WhenSuperUserExists));
        var group = await SeedAdminGroupAsync(db);
        var admin = await AddUserAsync(db, "admin1", groupId: group.Id);
        await AddUserAsync(db, "super1", isSuperUser: true);

        var guard = new PermissionLockoutGuard(db);
        Assert.False(await guard.WouldDeleteGroupLockoutAsync(admin.Id, group.Id));
    }

    [Fact]
    public async Task DeleteGroup_NoAdminPermission_NotGuarded()
    {
        await using var db = CreateContext(nameof(DeleteGroup_NoAdminPermission_NotGuarded));
        var group = new PermissionGroup { Name = "ReadOnly" };
        db.PermissionGroups.Add(group);
        await db.SaveChangesAsync();
        db.GroupPermissions.Add(new GroupPermission { GroupId = group.Id, PermissionKey = "assets.view", Value = PermissionValue.Grant });
        await db.SaveChangesAsync();
        var user = await AddUserAsync(db, "user1", groupId: group.Id);

        var guard = new PermissionLockoutGuard(db);
        Assert.False(await guard.WouldDeleteGroupLockoutAsync(user.Id, group.Id));
    }

    // =========================================================================
    // GUARD UNIT TESTS — WouldDemoteSuperUserLockoutAsync
    // =========================================================================

    [Fact]
    public async Task Demote_LastSuperUser_Blocked()
    {
        await using var db = CreateContext(nameof(Demote_LastSuperUser_Blocked));
        var actor = await AddUserAsync(db, "actor1");
        var super1 = await AddUserAsync(db, "super1", isSuperUser: true);

        var guard = new PermissionLockoutGuard(db);
        Assert.True(await guard.WouldDemoteSuperUserLockoutAsync(actor.Id, super1.Id));
    }

    [Fact]
    public async Task Demote_Allowed_WhenAnotherSuperUserExists()
    {
        await using var db = CreateContext(nameof(Demote_Allowed_WhenAnotherSuperUserExists));
        var actor = await AddUserAsync(db, "actor1");
        var super1 = await AddUserAsync(db, "super1", isSuperUser: true);
        await AddUserAsync(db, "super2", isSuperUser: true);

        var guard = new PermissionLockoutGuard(db);
        Assert.False(await guard.WouldDemoteSuperUserLockoutAsync(actor.Id, super1.Id));
    }

    [Fact]
    public async Task Demote_Allowed_WhenAdminRetainsCapability()
    {
        await using var db = CreateContext(nameof(Demote_Allowed_WhenAdminRetainsCapability));
        var group = await SeedAdminGroupAsync(db);
        var super1 = await AddUserAsync(db, "super1", isSuperUser: true);
        await AddUserAsync(db, "admin1", groupId: group.Id); // admin giữ khả năng quản lý sau khi hạ superuser

        var guard = new PermissionLockoutGuard(db);
        Assert.False(await guard.WouldDemoteSuperUserLockoutAsync(super1.Id, super1.Id));
    }

    [Fact]
    public async Task Demote_NonSuperUser_NotGuarded()
    {
        await using var db = CreateContext(nameof(Demote_NonSuperUser_NotGuarded));
        var actor = await AddUserAsync(db, "actor1");
        var regular = await AddUserAsync(db, "regular1");

        var guard = new PermissionLockoutGuard(db);
        Assert.False(await guard.WouldDemoteSuperUserLockoutAsync(actor.Id, regular.Id));
    }

    // =========================================================================
    // GUARD UNIT TESTS — WouldDeactivateUserLockoutAsync
    // =========================================================================

    [Fact]
    public async Task Deactivate_LastSuperUser_Blocked()
    {
        await using var db = CreateContext(nameof(Deactivate_LastSuperUser_Blocked));
        var actor = await AddUserAsync(db, "actor1");
        var super1 = await AddUserAsync(db, "super1", isSuperUser: true);

        var guard = new PermissionLockoutGuard(db);
        Assert.True(await guard.WouldDeactivateUserLockoutAsync(actor.Id, super1.Id));
    }

    [Fact]
    public async Task Deactivate_LastAdmin_Blocked()
    {
        await using var db = CreateContext(nameof(Deactivate_LastAdmin_Blocked));
        var group = await SeedAdminGroupAsync(db);
        var admin = await AddUserAsync(db, "admin1", groupId: group.Id);

        var guard = new PermissionLockoutGuard(db);
        Assert.True(await guard.WouldDeactivateUserLockoutAsync(admin.Id, admin.Id));
    }

    [Fact]
    public async Task Deactivate_Allowed_WhenAnotherManagerExists()
    {
        await using var db = CreateContext(nameof(Deactivate_Allowed_WhenAnotherManagerExists));
        var actor = await AddUserAsync(db, "actor1");
        await AddUserAsync(db, "super1", isSuperUser: true);
        var super2 = await AddUserAsync(db, "super2", isSuperUser: true);

        var guard = new PermissionLockoutGuard(db);
        Assert.False(await guard.WouldDeactivateUserLockoutAsync(actor.Id, super2.Id));
    }

    [Fact]
    public async Task Deactivate_NonManager_NotGuarded()
    {
        await using var db = CreateContext(nameof(Deactivate_NonManager_NotGuarded));
        var actor = await AddUserAsync(db, "actor1");
        var regular = await AddUserAsync(db, "regular1");

        var guard = new PermissionLockoutGuard(db);
        Assert.False(await guard.WouldDeactivateUserLockoutAsync(actor.Id, regular.Id));
    }

    // =========================================================================
    // CONTROLLER — GroupsController.DeleteGroup lockout
    // =========================================================================

    [Fact]
    public async Task DeleteGroup_Controller_LastAdminGroup_Returns400SelfLockout_GroupKept()
    {
        await using var db = CreateContext(nameof(DeleteGroup_Controller_LastAdminGroup_Returns400SelfLockout_GroupKept));
        var group = await SeedAdminGroupAsync(db);
        var admin = await AddUserAsync(db, "admin1", groupId: group.Id);

        // [Giai đoạn 3] Handler-level with the REAL guard — non-realm-superuser actor.
        var handler = new DeleteGroupCommandHandler(db, new PermissionLockoutGuard(db));
        var result = await handler.Handle(
            new DeleteGroupCommand(group.Id, admin.Id, ActorIsRealmSuperUser: false),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("SELF_LOCKOUT", result.ErrorCode);

        Assert.NotNull(await db.PermissionGroups.FindAsync(group.Id)); // không bị xóa
    }

    [Fact]
    public async Task DeleteGroup_Controller_AnotherAdminInDifferentGroup_Returns200_GroupDeleted()
    {
        await using var db = CreateContext(nameof(DeleteGroup_Controller_AnotherAdminInDifferentGroup_Returns200_GroupDeleted));
        var groupA = await SeedAdminGroupAsync(db, "AdminsA");
        var groupB = await SeedAdminGroupAsync(db, "AdminsB");
        var adminA = await AddUserAsync(db, "adminA", groupId: groupA.Id);
        await AddUserAsync(db, "adminB", groupId: groupB.Id);

        // [Giai đoạn 3] adminB (another group) still holds management capability → delete allowed.
        var handler = new DeleteGroupCommandHandler(db, new PermissionLockoutGuard(db));
        var result = await handler.Handle(
            new DeleteGroupCommand(groupA.Id, adminA.Id, ActorIsRealmSuperUser: false),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(await db.PermissionGroups.FindAsync(groupA.Id)); // đã xóa
    }

    // =========================================================================
    // UpdateUserCommand / DeleteUserCommand — company-scoping + lockout guard
    // [FIX BUG-M 2026-10-02] The guards moved OUT of UsersController INTO the command handlers
    // (the controller is now IMediator-only), so these tests drive the handlers directly with a REAL
    // PermissionLockoutGuard. Same substance; they now assert the outcome (result.ErrorCode / DB
    // state) instead of "reached the throwing mediator".
    // =========================================================================

    private static UpdateUserCommandHandler BuildUpdateHandler(AppDbContext db, TestHelpers.FakeScope scope)
        => new(db, scope, new PermissionLockoutGuard(db), NullLogger<UpdateUserCommandHandler>.Instance);

    private static DeleteUserCommandHandler BuildDeleteHandler(AppDbContext db, TestHelpers.FakeScope scope)
        => new(db, scope, new PermissionLockoutGuard(db), NullLogger<DeleteUserCommandHandler>.Instance);

    [Fact]
    public async Task UpdateUser_CrossCompany_Rejected_NotFound()
    {
        await using var db = CreateContext(nameof(UpdateUser_CrossCompany_Rejected_NotFound));
        var companyA = await AddCompanyAsync(db, "CT-A");
        var companyB = await AddCompanyAsync(db, "CT-B");
        var actor = await AddUserAsync(db, "actor1", companyId: companyA);
        var target = await AddUserAsync(db, "target1", companyId: companyB);

        var handler = BuildUpdateHandler(db, new TestHelpers.FakeScope { Super = false, CompanyId = companyA });
        var result = await handler.Handle(
            ValidUpdate(target.Id, false) with { CurrentUserId = actor.Id }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("USER_NOT_FOUND", result.ErrorCode); // hide-existence
    }

    [Fact]
    public async Task UpdateUser_SameCompany_Allowed()
    {
        await using var db = CreateContext(nameof(UpdateUser_SameCompany_Allowed));
        var companyA = await AddCompanyAsync(db, "CT-A");
        var actor = await AddUserAsync(db, "actor1", companyId: companyA);
        var target = await AddUserAsync(db, "target1", companyId: companyA);

        var handler = BuildUpdateHandler(db, new TestHelpers.FakeScope { Super = false, CompanyId = companyA });
        var result = await handler.Handle(
            ValidUpdate(target.Id, false) with { CurrentUserId = actor.Id }, CancellationToken.None);

        Assert.True(result.Success);
        var reloaded = await db.Users.SingleAsync(u => u.Id == target.Id);
        Assert.Equal("unique@t.local", reloaded.Email);
        Assert.Equal(companyA, reloaded.CompanyId); // unchanged (patch: CompanyId not sent)
    }

    [Fact]
    public async Task UpdateUser_SuperUserActor_CrossCompany_Allowed()
    {
        await using var db = CreateContext(nameof(UpdateUser_SuperUserActor_CrossCompany_Allowed));
        var companyA = await AddCompanyAsync(db, "CT-A");
        var companyB = await AddCompanyAsync(db, "CT-B");
        var actor = await AddUserAsync(db, "actor1", companyId: companyA);
        var target = await AddUserAsync(db, "target1", companyId: companyB);

        var handler = BuildUpdateHandler(db, new TestHelpers.FakeScope { Super = true });
        var result = await handler.Handle(
            ValidUpdate(target.Id, false) with { CurrentUserId = actor.Id }, CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task UpdateUser_DemoteLastSuperUser_SelfLockout()
    {
        await using var db = CreateContext(nameof(UpdateUser_DemoteLastSuperUser_SelfLockout));
        var companyA = await AddCompanyAsync(db, "CT-A");
        var actor = await AddUserAsync(db, "actor1", companyId: companyA);
        var super1 = await AddUserAsync(db, "super1", companyId: companyA, isSuperUser: true);

        var handler = BuildUpdateHandler(db, new TestHelpers.FakeScope { Super = false, CompanyId = companyA });
        var result = await handler.Handle(
            ValidUpdate(super1.Id, false) with { CurrentUserId = actor.Id }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("SELF_LOCKOUT", result.ErrorCode);
        Assert.True((await db.Users.SingleAsync(u => u.Id == super1.Id)).IsSuperUser); // không bị hạ cờ
    }

    [Fact]
    public async Task UpdateUser_DemoteSuperUser_WhenAnotherSuperUserExists_Allowed()
    {
        await using var db = CreateContext(nameof(UpdateUser_DemoteSuperUser_WhenAnotherSuperUserExists_Allowed));
        var companyA = await AddCompanyAsync(db, "CT-A");
        var actor = await AddUserAsync(db, "actor1", companyId: companyA);
        var super1 = await AddUserAsync(db, "super1", companyId: companyA, isSuperUser: true);
        await AddUserAsync(db, "super2", companyId: companyA, isSuperUser: true);

        var handler = BuildUpdateHandler(db, new TestHelpers.FakeScope { Super = false, CompanyId = companyA });
        var result = await handler.Handle(
            ValidUpdate(super1.Id, false) with { CurrentUserId = actor.Id }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.False((await db.Users.SingleAsync(u => u.Id == super1.Id)).IsSuperUser);
    }

    // =========================================================================
    // DeleteUserCommand — company-scoping + deactivate lockout (guards moved into the handler)
    // =========================================================================

    [Fact]
    public async Task DeleteUser_CrossCompany_Rejected_NotFound()
    {
        await using var db = CreateContext(nameof(DeleteUser_CrossCompany_Rejected_NotFound));
        var companyA = await AddCompanyAsync(db, "CT-A");
        var companyB = await AddCompanyAsync(db, "CT-B");
        var actor = await AddUserAsync(db, "actor1", companyId: companyA);
        var target = await AddUserAsync(db, "target1", companyId: companyB);

        var handler = BuildDeleteHandler(db, new TestHelpers.FakeScope { Super = false, CompanyId = companyA });
        var result = await handler.Handle(new DeleteUserCommand(target.Id, actor.Id), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("USER_NOT_FOUND", result.ErrorCode); // hide-existence
        Assert.True((await db.Users.SingleAsync(u => u.Id == target.Id)).IsActive); // không bị vô hiệu hóa
    }

    [Fact]
    public async Task DeleteUser_DeactivateLastSuperUser_SelfLockout()
    {
        await using var db = CreateContext(nameof(DeleteUser_DeactivateLastSuperUser_SelfLockout));
        var companyA = await AddCompanyAsync(db, "CT-A");
        var actor = await AddUserAsync(db, "actor1", companyId: companyA);
        var super1 = await AddUserAsync(db, "super1", companyId: companyA, isSuperUser: true);

        var handler = BuildDeleteHandler(db, new TestHelpers.FakeScope { Super = false, CompanyId = companyA });
        var result = await handler.Handle(new DeleteUserCommand(super1.Id, actor.Id), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("SELF_LOCKOUT", result.ErrorCode);
        Assert.True((await db.Users.SingleAsync(u => u.Id == super1.Id)).IsActive);
    }

    [Fact]
    public async Task DeleteUser_DeactivateSuperUser_WhenAnotherManagerExists_Allowed()
    {
        await using var db = CreateContext(nameof(DeleteUser_DeactivateSuperUser_WhenAnotherManagerExists_Allowed));
        var companyA = await AddCompanyAsync(db, "CT-A");
        var actor = await AddUserAsync(db, "actor1", companyId: companyA);
        var super1 = await AddUserAsync(db, "super1", companyId: companyA, isSuperUser: true);
        await AddUserAsync(db, "super2", companyId: companyA, isSuperUser: true);

        var handler = BuildDeleteHandler(db, new TestHelpers.FakeScope { Super = false, CompanyId = companyA });
        var result = await handler.Handle(new DeleteUserCommand(super1.Id, actor.Id), CancellationToken.None);

        Assert.True(result.Success);
        Assert.False((await db.Users.SingleAsync(u => u.Id == super1.Id)).IsActive);
    }
}
