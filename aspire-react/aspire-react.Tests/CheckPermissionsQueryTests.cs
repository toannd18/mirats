using aspire_react.Server.Application.Permissions.Queries;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [Giai đoạn 3 — Permissions] CheckPermissionsQueryHandler unit tests — đường phân quyền
/// quan trọng nhất (frontend usePermission hook gọi mỗi session). Phủ 2 hành vi verbatim:
/// (1) Deny-override merge — direct UserPermission Deny (-1) ưu tiên hơn Group Grant;
/// (2) user-null → EMPTY dict + false/false (KHÔNG 404 — hành vi đã có từ trước, khó test
/// qua API thật vì cần JWT của user không tồn tại trong DB).
/// API-level smoke (superuser shape parity) chạy trong verify script; Deny-override qua API
/// với fixture user fresh KHÔNG khả thi (CreateUserCommand không set password Keycloak →
/// không login được) → unit test là lớp phủ chính cho merge logic (disclosed trong báo cáo).
/// </summary>
public class CheckPermissionsQueryTests
{
    private static AppDbContext CreateContext(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options, new TestHelpers.SuperUserScope());
    }

    [Fact]
    public async Task Deny_Overrides_GroupGrant_And_DirectGrant()
    {
        await using var db = CreateContext(nameof(Deny_Overrides_GroupGrant_And_DirectGrant));
        var group = new PermissionGroup { Name = "Viewers" };
        var user = new User { Username = "denied", Email = "denied@local" };
        db.PermissionGroups.Add(group);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        // Group grants assets.view + assets.edit; user ALSO has group with assets.checkout grant.
        db.GroupPermissions.AddRange(
            new GroupPermission { GroupId = group.Id, PermissionKey = "assets.view", Value = PermissionValue.Grant },
            new GroupPermission { GroupId = group.Id, PermissionKey = "assets.edit", Value = PermissionValue.Grant });
        // Direct: assets.view DENIED (must override group Grant), assets.create GRANTED directly.
        db.UserPermissions.AddRange(
            new UserPermission { UserId = user.Id, PermissionKey = "assets.view", Value = PermissionValue.Deny },
            new UserPermission { UserId = user.Id, PermissionKey = "assets.create", Value = PermissionValue.Grant });
        db.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = group.Id });
        await db.SaveChangesAsync();

        var handler = new CheckPermissionsQueryHandler(db);
        var dto = await handler.Handle(
            new CheckPermissionsQuery(user.Id, IsRealmSuperUser: false), CancellationToken.None);

        Assert.False(dto.IsSuperUser);
        // Deny -1 survives (group Grant must NOT overwrite it) — THE Deny-override invariant.
        Assert.True(dto.Permissions.ContainsKey("assets.view"));
        Assert.Equal(-1, dto.Permissions["assets.view"]);
        // Group Grant applies where no direct permission exists.
        Assert.Equal(1, dto.Permissions["assets.edit"]);
        // Direct Grant applies.
        Assert.Equal(1, dto.Permissions["assets.create"]);
        // Not admin (no admin key).
        Assert.False(dto.IsAdmin);
    }

    [Fact]
    public async Task UnknownUser_ReturnsEmptyPermissions_FalseFalse_No404()
    {
        await using var db = CreateContext(nameof(UnknownUser_ReturnsEmptyPermissions_FalseFalse_No404));
        var handler = new CheckPermissionsQueryHandler(db);

        // User id not present in DB → EMPTY dict + false/false (pre-migration verbatim, NOT 404).
        var dto = await handler.Handle(
            new CheckPermissionsQuery(Guid.NewGuid(), IsRealmSuperUser: false), CancellationToken.None);

        Assert.Empty(dto.Permissions);
        Assert.False(dto.IsSuperUser);
        Assert.False(dto.IsAdmin);
    }

    [Fact]
    public async Task RealmSuperUser_Flag_SetsSuperuser_And_IsSuperUser_True()
    {
        await using var db = CreateContext(nameof(RealmSuperUser_Flag_SetsSuperuser_And_IsSuperUser_True));
        var user = new User { Username = "plainadmin", Email = "pa@local", IsSuperUser = false };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // DB flag false but realm role says superuser (mirror PermissionHandler step 1) —
        // the realm flag is resolved by the CONTROLLER from HttpContext and passed in.
        var handler = new CheckPermissionsQueryHandler(db);
        var dto = await handler.Handle(
            new CheckPermissionsQuery(user.Id, IsRealmSuperUser: true), CancellationToken.None);

        Assert.Equal(1, dto.Permissions["superuser"]);
        Assert.True(dto.IsSuperUser);
    }
}
