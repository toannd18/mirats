using System.Security.Claims;
using System.Text.Json;
using aspire_react.Server.Application.Groups.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Authorization;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using aspire_react.Server.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// PermissionHandler authorization chain (Keycloak superuser → local IsSuperUser →
/// UserPermission Grant/Deny → GroupPermission Grant → admin wildcard → Default Deny),
/// the local_user_id resolution fix, removal of the auto-create side-effect, the
/// permission catalog endpoint, and system-group guards.
/// </summary>
public class PermissionTests
{
    private static AppDbContext CreateContext(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(options);
    }

    private static ClaimsPrincipal CreatePrincipal(
        bool superUserRole = false,
        string? localUserId = null,
        string username = "user1")
    {
        var claims = new List<Claim>
        {
            new("preferred_username", username),
            new(ClaimTypes.NameIdentifier, "kc-sub-" + username),
            new("realm_access", superUserRole ? "{\"roles\":[\"admin\"]}" : "{\"roles\":[\"user\"]}")
        };
        if (localUserId != null)
            claims.Add(new Claim("local_user_id", localUserId));
        var identity = new ClaimsIdentity(claims, "Test");
        return new ClaimsPrincipal(identity);
    }

    private static async Task<bool> EvaluateAsync(AppDbContext db, ClaimsPrincipal principal, string permissionKey)
    {
        var requirement = new PermissionRequirement(permissionKey);
        var authContext = new AuthorizationHandlerContext(
            new IAuthorizationRequirement[] { requirement }, principal, resource: null);
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
        var handler = new PermissionHandler(db, httpContextAccessor);
        await handler.HandleAsync(authContext);
        return authContext.HasSucceeded;
    }

    private static async Task<Guid> SeedUserWithGroupGrantAsync(
        AppDbContext db,
        string permissionKey)
    {
        var user = new User { Username = "user1", Email = "user1@local" };
        var group = new PermissionGroup { Name = "Editors" };
        db.Users.Add(user);
        db.PermissionGroups.Add(group);
        await db.SaveChangesAsync();
        db.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = group.Id });
        db.GroupPermissions.Add(new GroupPermission { GroupId = group.Id, PermissionKey = permissionKey, Value = PermissionValue.Grant });
        await db.SaveChangesAsync();
        return user.Id;
    }

    // ==================== 1. Superuser / Admin bypass (Keycloak realm role) ====================

    [Fact]
    public async Task Superuser_RealmRole_BypassesAllPermissionKeys()
    {
        await using var db = CreateContext(nameof(Superuser_RealmRole_BypassesAllPermissionKeys));
        var principal = CreatePrincipal(superUserRole: true);

        Assert.True(await EvaluateAsync(db, principal, "assets.delete"));
        Assert.True(await EvaluateAsync(db, principal, "licenses.view"));
        Assert.True(await EvaluateAsync(db, principal, "admin"));
        Assert.True(await EvaluateAsync(db, principal, "customfields.delete"));
    }

    // ==================== 2. Local IsSuperUser bypass ====================

    [Fact]
    public async Task LocalUser_IsSuperUser_BypassesAllPermissionKeys()
    {
        await using var db = CreateContext(nameof(LocalUser_IsSuperUser_BypassesAllPermissionKeys));
        var user = new User { Username = "user1", Email = "user1@local", IsSuperUser = true };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var principal = CreatePrincipal(localUserId: user.Id.ToString());
        Assert.True(await EvaluateAsync(db, principal, "assets.delete"));
        Assert.True(await EvaluateAsync(db, principal, "customfields.delete"));
    }

    // ==================== 3. UserPermission Grant ====================

    [Fact]
    public async Task UserPermissionGrant_GrantsPermission()
    {
        await using var db = CreateContext(nameof(UserPermissionGrant_GrantsPermission));
        var user = new User { Username = "user1", Email = "user1@local" };
        db.Users.Add(user);
        db.UserPermissions.Add(new UserPermission { UserId = user.Id, PermissionKey = "assets.view", Value = PermissionValue.Grant });
        await db.SaveChangesAsync();

        var principal = CreatePrincipal(localUserId: user.Id.ToString());
        Assert.True(await EvaluateAsync(db, principal, "assets.view"));
        Assert.False(await EvaluateAsync(db, principal, "assets.delete"));
    }

    // ==================== 4. UserPermission Deny overrides Group Grant ====================

    [Fact]
    public async Task UserPermissionDeny_OverridesGroupGrant()
    {
        await using var db = CreateContext(nameof(UserPermissionDeny_OverridesGroupGrant));
        var userId = await SeedUserWithGroupGrantAsync(db, "assets.view");
        db.UserPermissions.Add(new UserPermission { UserId = userId, PermissionKey = "assets.view", Value = PermissionValue.Deny });
        await db.SaveChangesAsync();

        var principal = CreatePrincipal(localUserId: userId.ToString());
        Assert.False(await EvaluateAsync(db, principal, "assets.view"));
    }

    // ==================== 5. GroupPermission Grant ====================

    [Fact]
    public async Task GroupPermissionGrant_GrantsPermission()
    {
        await using var db = CreateContext(nameof(GroupPermissionGrant_GrantsPermission));
        var userId = await SeedUserWithGroupGrantAsync(db, "assets.view");

        var principal = CreatePrincipal(localUserId: userId.ToString());
        Assert.True(await EvaluateAsync(db, principal, "assets.view"));
        Assert.False(await EvaluateAsync(db, principal, "assets.delete"));
    }

    // ==================== 6. Admin wildcard ====================

    [Fact]
    public async Task AdminPermissionWildcard_GrantsOtherPermissions()
    {
        await using var db = CreateContext(nameof(AdminPermissionWildcard_GrantsOtherPermissions));
        var user = new User { Username = "user1", Email = "user1@local" };
        db.Users.Add(user);
        db.UserPermissions.Add(new UserPermission { UserId = user.Id, PermissionKey = "admin", Value = PermissionValue.Grant });
        await db.SaveChangesAsync();

        var principal = CreatePrincipal(localUserId: user.Id.ToString());
        Assert.True(await EvaluateAsync(db, principal, "assets.view"));
        Assert.True(await EvaluateAsync(db, principal, "users.edit"));
    }

    // ==================== 7. Default deny ====================

    [Fact]
    public async Task NoPermission_DefaultDeny()
    {
        await using var db = CreateContext(nameof(NoPermission_DefaultDeny));
        var user = new User { Username = "user1", Email = "user1@local" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var principal = CreatePrincipal(localUserId: user.Id.ToString());
        Assert.False(await EvaluateAsync(db, principal, "assets.view"));
        Assert.False(await EvaluateAsync(db, principal, "users.edit"));
    }

    // ==================== 8. local_user_id resolution (regression: username renames) ====================

    [Fact]
    public async Task UserResolvedViaLocalUserId_WhenUsernameDiffers()
    {
        await using var db = CreateContext(nameof(UserResolvedViaLocalUserId_WhenUsernameDiffers));
        // Local user "alice" — but the Keycloak token now carries a DIFFERENT preferred_username.
        var user = new User { Username = "alice", Email = "alice@local" };
        var group = new PermissionGroup { Name = "Editors" };
        db.Users.Add(user);
        db.PermissionGroups.Add(group);
        await db.SaveChangesAsync();
        db.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = group.Id });
        db.GroupPermissions.Add(new GroupPermission { GroupId = group.Id, PermissionKey = "assets.view", Value = PermissionValue.Grant });
        await db.SaveChangesAsync();

        // JIT provisioning stamped local_user_id; preferred_username is stale/renamed.
        var principal = CreatePrincipal(localUserId: user.Id.ToString(), username: "alice_renamed");

        // Must resolve via local_user_id → group grant applies. (Old code failed here.)
        Assert.True(await EvaluateAsync(db, principal, "assets.view"));
    }

    // ==================== 9. Unknown user → fail closed, no auto-create ====================

    [Fact]
    public async Task UnknownUser_Fails_WithoutAutoCreate()
    {
        await using var db = CreateContext(nameof(UnknownUser_Fails_WithoutAutoCreate));
        var principal = CreatePrincipal(localUserId: Guid.NewGuid().ToString(), username: "ghost");

        Assert.False(await EvaluateAsync(db, principal, "assets.view"));

        // The authorization handler must NOT have created any user as a side-effect.
        Assert.Equal(0, await db.Users.CountAsync());
    }

    // ==================== 10. Permission catalog endpoint ====================

    [Fact]
    public void GetPermissions_ReturnsCompleteGroupedCatalog()
    {
        using var db = CreateContext(nameof(GetPermissions_ReturnsCompleteGroupedCatalog));
        var controller = new PermissionsController(db);

        var result = controller.GetPermissions();
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var root = doc.RootElement;
        Assert.Equal("success", root.GetProperty("status").GetString());

        var data = root.GetProperty("data");
        Assert.True(data.GetArrayLength() > 0);

        var codes = data.EnumerateArray()
            .SelectMany(g => g.GetProperty("permissions").EnumerateArray()
                .Select(p => p.GetProperty("code").GetString()!))
            .ToList();

        // The previously-missing policy is now present in the catalog.
        Assert.Contains("customfields.delete", codes);
        Assert.Contains("assets.view", codes);
        Assert.Contains("admin", codes);
        Assert.Contains("superuser", codes);

        // Complete + no duplicates.
        Assert.Equal(PermissionCatalog.All.Count, codes.Count);
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    // ==================== 11. System groups cannot be renamed/deleted ====================

    [Fact]
    public async Task SystemGroup_DeleteAndRename_Blocked()
    {
        await using var db = CreateContext(nameof(SystemGroup_DeleteAndRename_Blocked));
        var systemGroup = new PermissionGroup { Name = "Superuser", IsSystem = true };
        var normalGroup = new PermissionGroup { Name = "Editors" };
        db.PermissionGroups.AddRange(systemGroup, normalGroup);
        await db.SaveChangesAsync();

        // [Giai đoạn 3] Groups migrated to MediatR — drive the command handlers directly
        // (same substance: SYSTEM_GROUP_LOCKED blocks rename/delete; normal group deletable).
        var updateHandler = new UpdateGroupCommandHandler(db);
        var deleteHandler = new DeleteGroupCommandHandler(db, new PermissionLockoutGuard(db));

        // Rename system group → SYSTEM_GROUP_LOCKED
        var renameResult = await updateHandler.Handle(
            new UpdateGroupCommand(systemGroup.Id, "Hacked", null, Guid.NewGuid()), CancellationToken.None);
        Assert.False(renameResult.Success);
        Assert.Equal("SYSTEM_GROUP_LOCKED", renameResult.ErrorCode);
        Assert.Equal("Superuser", (await db.PermissionGroups.FindAsync(systemGroup.Id))!.Name);

        // Delete system group → SYSTEM_GROUP_LOCKED
        var deleteResult = await deleteHandler.Handle(
            new DeleteGroupCommand(systemGroup.Id, Guid.NewGuid(), ActorIsRealmSuperUser: false), CancellationToken.None);
        Assert.False(deleteResult.Success);
        Assert.Equal("SYSTEM_GROUP_LOCKED", deleteResult.ErrorCode);

        // Normal group is still deletable
        var normalDelete = await deleteHandler.Handle(
            new DeleteGroupCommand(normalGroup.Id, Guid.NewGuid(), ActorIsRealmSuperUser: false), CancellationToken.None);
        Assert.True(normalDelete.Success);
    }
}
