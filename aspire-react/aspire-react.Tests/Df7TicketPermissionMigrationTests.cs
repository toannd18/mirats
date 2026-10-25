using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [DF-7] The ticket-permission data migration must run exactly once. The SystemSetting flag is
/// authoritative: removing a grant later must NOT cause the next startup to re-add it.
/// </summary>
public class Df7TicketPermissionMigrationTests
{
    private static async Task<(AppDbContext ctx, Guid groupWithEdit, Guid groupWithoutEdit, Guid actor)> SeedAsync(string name)
    {
        var ctx = TestHelpers.CreateContext(name);
        var actor = new User
        {
            Username = "admin-df7",
            Email = "admin-df7@test.local",
            FirstName = "Admin",
            LastName = "DF7",
            IsSuperUser = true,
            IsActive = true,
        };
        var withEdit = new PermissionGroup { Name = "Tech DF7" };
        var withoutEdit = new PermissionGroup { Name = "Other DF7" };
        ctx.Users.Add(actor);
        ctx.PermissionGroups.AddRange(withEdit, withoutEdit);
        ctx.GroupPermissions.AddRange(
            new GroupPermission { GroupId = withEdit.Id, PermissionKey = "assets.edit", Value = PermissionValue.Grant },
            new GroupPermission { GroupId = withoutEdit.Id, PermissionKey = "assets.view", Value = PermissionValue.Grant });
        await ctx.SaveChangesAsync();
        return (ctx, withEdit.Id, withoutEdit.Id, actor.Id);
    }

    [Fact]
    public async Task Migration_GrantsBothKeysOnce_AndDoesNotRegrantAfterFlagExists()
    {
        var (ctx, withEdit, withoutEdit, _) = await SeedAsync(nameof(Migration_GrantsBothKeysOnce_AndDoesNotRegrantAfterFlagExists));
        await using var db = ctx;

        StartupDataSeeder.MigrateDf7TicketPermissions(db);

        Assert.Equal(1, await db.GroupPermissions.CountAsync(p =>
            p.GroupId == withEdit && p.PermissionKey == "maintenance.tickets"));
        Assert.Equal(1, await db.GroupPermissions.CountAsync(p =>
            p.GroupId == withEdit && p.PermissionKey == "maintenance.tickets.approve"));
        Assert.False(await db.GroupPermissions.AnyAsync(p =>
            p.GroupId == withoutEdit && p.PermissionKey == "maintenance.tickets"));
        Assert.True(await db.SystemSettings.AnyAsync(s => s.Key == StartupDataSeeder.Df7TicketPermissionsFlag));
        Assert.Equal(2, await db.ActionLogs.CountAsync(l => l.ItemType == ItemType.PermissionGroup));

        // Admin removes one key through the normal API later; a restart must NOT restore it.
        var removed = await db.GroupPermissions.SingleAsync(p =>
            p.GroupId == withEdit && p.PermissionKey == "maintenance.tickets");
        db.GroupPermissions.Remove(removed);
        await db.SaveChangesAsync();

        StartupDataSeeder.MigrateDf7TicketPermissions(db);
        StartupDataSeeder.MigrateDf7TicketPermissions(db);

        Assert.False(await db.GroupPermissions.AnyAsync(p =>
            p.GroupId == withEdit && p.PermissionKey == "maintenance.tickets"));
        Assert.Equal(1, await db.GroupPermissions.CountAsync(p =>
            p.GroupId == withEdit && p.PermissionKey == "maintenance.tickets.approve"));
    }
}
