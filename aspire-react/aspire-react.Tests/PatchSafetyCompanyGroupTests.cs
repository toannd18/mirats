using aspire_react.Server.Application.Companies.Commands;
using aspire_react.Server.Application.Groups.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Authorization;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [FIX-E 2026-10-02] Patch-safety for the three remaining offenders found by the audit
/// (N3 + N4 + N10), same bug class as BUG-E/N ("absent field must never be written"):
///   * N3 UpdateCompanyCommand — Name/ParentId were assigned unconditionally: a partial payload sent
///     Name=null (DB NOT NULL → raw 500) and ParentId=null (company silently re-rooted). A rename
///     colliding with the unique index also surfaced as 500 instead of 400.
///   * N4 UpdateGroupCommand — Description was assigned unconditionally → cleared by a rename-only PUT.
///   * N10 UpdateGroupPermissionsCommand — an absent/null `permissions` threw NullReferenceException
///     (raw 500) instead of a 400.
/// </summary>
public class PatchSafetyCompanyGroupTests
{
    private static readonly Guid ActorId = Guid.NewGuid();

    private static AppDbContext CreateContext(string name) => TestHelpers.CreateContext(name);

    // =========================================================================
    // N3 — UpdateCompanyCommand
    // =========================================================================

    [Fact]
    public async Task Company_Update_PartialPayload_KeepsNameAndParent()
    {
        await using var ctx = CreateContext(nameof(Company_Update_PartialPayload_KeepsNameAndParent));
        var parent = new Company { Name = "Parent Co", Code = "PAR" };
        ctx.Companies.Add(parent);
        await ctx.SaveChangesAsync();
        var child = new Company { Name = "Child Co", Code = "CHI", ParentId = parent.Id };
        ctx.Companies.Add(child);
        await ctx.SaveChangesAsync();

        var handler = new UpdateCompanyCommandHandler(ctx, new TestHelpers.FakeScope { Super = true });

        // Partial payload: only the code is sent (Name/ParentId absent) — the old code nulled both.
        var result = await handler.Handle(
            new UpdateCompanyCommand(child.Id, Name: null, ParentId: null, Code: "CHI2", CurrentUserId: ActorId),
            CancellationToken.None);

        Assert.True(result.Success);
        var reloaded = await ctx.Companies.SingleAsync(c => c.Id == child.Id);
        Assert.Equal("Child Co", reloaded.Name);   // NOT nulled (was a NOT NULL 500)
        Assert.Equal(parent.Id, reloaded.ParentId); // NOT re-rooted
        Assert.Equal("CHI2", reloaded.Code);        // sent field applied
    }

    [Fact]
    public async Task Company_Update_BlankName_Rejected_NotSaved()
    {
        await using var ctx = CreateContext(nameof(Company_Update_BlankName_Rejected_NotSaved));
        var co = new Company { Name = "Keep Me", Code = "KM" };
        ctx.Companies.Add(co);
        await ctx.SaveChangesAsync();

        var handler = new UpdateCompanyCommandHandler(ctx, new TestHelpers.FakeScope { Super = true });
        var result = await handler.Handle(
            new UpdateCompanyCommand(co.Id, Name: "   ", ParentId: null, Code: null, CurrentUserId: ActorId),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Tên công ty không được để trống.", result.Message);
        Assert.Equal("Keep Me", (await ctx.Companies.SingleAsync(c => c.Id == co.Id)).Name);
    }

    [Fact]
    public async Task Company_Update_DuplicateName_Rejected_NotSaved()
    {
        await using var ctx = CreateContext(nameof(Company_Update_DuplicateName_Rejected_NotSaved));
        ctx.Companies.Add(new Company { Name = "Taken", Code = "T1" });
        var other = new Company { Name = "Mine", Code = "M1" };
        ctx.Companies.Add(other);
        await ctx.SaveChangesAsync();

        var handler = new UpdateCompanyCommandHandler(ctx, new TestHelpers.FakeScope { Super = true });
        var result = await handler.Handle(
            new UpdateCompanyCommand(other.Id, Name: "Taken", ParentId: null, Code: null, CurrentUserId: ActorId),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("đã tồn tại", result.Message);
        Assert.Equal("Mine", (await ctx.Companies.SingleAsync(c => c.Id == other.Id)).Name);
    }

    [Fact]
    public async Task Company_Update_ParentIdEmptySentinel_ReRoots()
    {
        await using var ctx = CreateContext(nameof(Company_Update_ParentIdEmptySentinel_ReRoots));
        var parent = new Company { Name = "P", Code = "P1" };
        ctx.Companies.Add(parent);
        await ctx.SaveChangesAsync();
        var child = new Company { Name = "C", Code = "C1", ParentId = parent.Id };
        ctx.Companies.Add(child);
        await ctx.SaveChangesAsync();

        var handler = new UpdateCompanyCommandHandler(ctx, new TestHelpers.FakeScope { Super = true });
        var result = await handler.Handle(
            new UpdateCompanyCommand(child.Id, Name: null, ParentId: Guid.Empty, Code: null, CurrentUserId: ActorId),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null((await ctx.Companies.SingleAsync(c => c.Id == child.Id)).ParentId); // explicitly re-rooted
    }

    [Fact]
    public async Task Company_Update_CircularParent_Rejected()
    {
        await using var ctx = CreateContext(nameof(Company_Update_CircularParent_Rejected));
        var child = new Company { Name = "C", Code = "C1" };
        ctx.Companies.Add(child);
        await ctx.SaveChangesAsync();

        var handler = new UpdateCompanyCommandHandler(ctx, new TestHelpers.FakeScope { Super = true });
        var result = await handler.Handle(
            new UpdateCompanyCommand(child.Id, Name: null, ParentId: child.Id, Code: null, CurrentUserId: ActorId),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("công ty con", result.Message);
    }

    // =========================================================================
    // N4 — UpdateGroupCommand
    // =========================================================================

    [Fact]
    public async Task Group_Update_PartialPayload_KeepsDescription()
    {
        await using var ctx = CreateContext(nameof(Group_Update_PartialPayload_KeepsDescription));
        var group = new PermissionGroup { Name = "Old Name", Description = "keep me" };
        ctx.PermissionGroups.Add(group);
        await ctx.SaveChangesAsync();

        var handler = new UpdateGroupCommandHandler(ctx);
        var result = await handler.Handle(
            new UpdateGroupCommand(group.Id, Name: "New Name", Description: null, CurrentUserId: ActorId),
            CancellationToken.None);

        Assert.True(result.Success);
        var reloaded = await ctx.PermissionGroups.SingleAsync(g => g.Id == group.Id);
        Assert.Equal("New Name", reloaded.Name);
        Assert.Equal("keep me", reloaded.Description); // NOT cleared by the rename-only payload
    }

    [Fact]
    public async Task Group_Update_SentEmptyDescription_Clears()
    {
        await using var ctx = CreateContext(nameof(Group_Update_SentEmptyDescription_Clears));
        var group = new PermissionGroup { Name = "G", Description = "clear me" };
        ctx.PermissionGroups.Add(group);
        await ctx.SaveChangesAsync();

        var handler = new UpdateGroupCommandHandler(ctx);
        var result = await handler.Handle(
            new UpdateGroupCommand(group.Id, Name: null, Description: "", CurrentUserId: ActorId),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(string.Empty, (await ctx.PermissionGroups.SingleAsync(g => g.Id == group.Id)).Description);
    }

    [Fact]
    public async Task Group_Update_BlankName_Rejected()
    {
        await using var ctx = CreateContext(nameof(Group_Update_BlankName_Rejected));
        var group = new PermissionGroup { Name = "G", Description = "d" };
        ctx.PermissionGroups.Add(group);
        await ctx.SaveChangesAsync();

        var handler = new UpdateGroupCommandHandler(ctx);
        var result = await handler.Handle(
            new UpdateGroupCommand(group.Id, Name: "  ", Description: "d2", CurrentUserId: ActorId),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Group name is required.", result.Message);
    }

    // =========================================================================
    // N10 — UpdateGroupPermissionsCommand
    // =========================================================================

    [Fact]
    public async Task GroupPermissions_NullList_RejectedWith400Message_NoThrow()
    {
        await using var ctx = CreateContext(nameof(GroupPermissions_NullList_RejectedWith400Message_NoThrow));
        var group = new PermissionGroup { Name = "G" };
        ctx.PermissionGroups.Add(group);
        await ctx.SaveChangesAsync();
        ctx.GroupPermissions.Add(new GroupPermission
        {
            GroupId = group.Id,
            PermissionKey = "assets.view",
            Value = PermissionValue.Grant
        });
        await ctx.SaveChangesAsync();

        var handler = new UpdateGroupPermissionsCommandHandler(ctx, new PermissionLockoutGuard(ctx));
        var result = await handler.Handle(
            new UpdateGroupPermissionsCommand(group.Id, Permissions: null, CurrentUserId: ActorId, ActorIsRealmSuperUser: true),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("không được để trống", result.Message);
        // existing permissions untouched (no clear-all by accident)
        Assert.Single(await ctx.GroupPermissions.Where(gp => gp.GroupId == group.Id).ToListAsync());
    }

    [Fact]
    public async Task GroupPermissions_EmptyArray_ClearsAll()
    {
        await using var ctx = CreateContext(nameof(GroupPermissions_EmptyArray_ClearsAll));
        var group = new PermissionGroup { Name = "G" };
        ctx.PermissionGroups.Add(group);
        await ctx.SaveChangesAsync();
        ctx.GroupPermissions.Add(new GroupPermission
        {
            GroupId = group.Id,
            PermissionKey = "assets.view",
            Value = PermissionValue.Grant
        });
        await ctx.SaveChangesAsync();

        var handler = new UpdateGroupPermissionsCommandHandler(ctx, new PermissionLockoutGuard(ctx));
        var result = await handler.Handle(
            new UpdateGroupPermissionsCommand(group.Id, Array.Empty<GroupPermissionEntry>(), ActorId, true),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Empty(await ctx.GroupPermissions.Where(gp => gp.GroupId == group.Id).ToListAsync());
    }
}
