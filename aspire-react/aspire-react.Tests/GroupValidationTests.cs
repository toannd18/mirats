using aspire_react.Server.Application.Groups.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [BUG-K FIX] Groups Create/Update validation — behavior changes approved (user-facing UI):
///   create: empty name → 400 "Group name is required."; duplicate name (case-insensitive) →
///           400 "A group with this name already exists."; valid create succeeds.
///   update: rename onto an existing name (different case) → rejected; re-sending the SAME name
///           (self) is a no-op success; system groups still SYSTEM_GROUP_LOCKED (guard order:
///           system-lock checked BEFORE validation).
/// </summary>
public class GroupValidationTests
{
    private static Guid ActorId { get; } = Guid.NewGuid();

    private static async Task<PermissionGroup> SeedGroupAsync(
        aspire_react.Server.Application.Common.Interfaces.IApplicationDbContext ctx, string name, bool isSystem = false)
    {
        var g = new PermissionGroup { Name = name, Description = null, IsSystem = isSystem };
        ctx.PermissionGroups.Add(g);
        await ctx.SaveChangesAsync();
        return g;
    }

    [Fact]
    public async Task Create_EmptyName_Rejected()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Create_EmptyName_Rejected));
        var handler = new CreateGroupCommandHandler(ctx);

        var result = await handler.Handle(new CreateGroupCommand("   ", null, ActorId), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Group name is required.", result.Message);
        Assert.Equal(0, await ctx.PermissionGroups.CountAsync());
    }

    [Fact]
    public async Task Create_DuplicateName_CaseInsensitive_Rejected_BugRepro()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Create_DuplicateName_CaseInsensitive_Rejected_BugRepro));
        await SeedGroupAsync(ctx, "Technicians");
        var handler = new CreateGroupCommandHandler(ctx);

        // BUG-K #1 reproduction: same name — now also blocked in different casing.
        var exact = await handler.Handle(new CreateGroupCommand("Technicians", null, ActorId), CancellationToken.None);
        Assert.False(exact.Success);
        Assert.Equal("A group with this name already exists.", exact.Message);

        var ci = await handler.Handle(new CreateGroupCommand("TECHNICIANS", null, ActorId), CancellationToken.None);
        Assert.False(ci.Success);
        Assert.Equal("A group with this name already exists.", ci.Message);

        Assert.Equal(1, await ctx.PermissionGroups.CountAsync());
    }

    [Fact]
    public async Task Create_ValidName_Succeeds()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Create_ValidName_Succeeds));
        await SeedGroupAsync(ctx, "Technicians");
        var handler = new CreateGroupCommandHandler(ctx);

        var result = await handler.Handle(new CreateGroupCommand("Managers", "desc", ActorId), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, await ctx.PermissionGroups.CountAsync());
    }

    [Fact]
    public async Task Update_RenameOntoExisting_CaseInsensitive_Rejected_SelfOk()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Update_RenameOntoExisting_CaseInsensitive_Rejected_SelfOk));
        var g1 = await SeedGroupAsync(ctx, "Technicians");
        var g2 = await SeedGroupAsync(ctx, "Managers");
        var handler = new UpdateGroupCommandHandler(ctx);

        var dup = await handler.Handle(new UpdateGroupCommand(g1.Id, "managers", null, ActorId), CancellationToken.None);
        Assert.False(dup.Success);
        Assert.Equal("A group with this name already exists.", dup.Message);

        var self = await handler.Handle(new UpdateGroupCommand(g1.Id, "Technicians", "new desc", ActorId), CancellationToken.None);
        Assert.True(self.Success);
        Assert.Equal("new desc", (await ctx.PermissionGroups.SingleAsync(x => x.Id == g1.Id)).Description);
    }

    [Fact]
    public async Task Update_SystemGroup_StillLocked_BeforeValidation()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Update_SystemGroup_StillLocked_BeforeValidation));
        var sys = await SeedGroupAsync(ctx, "Admin", isSystem: true);
        var handler = new UpdateGroupCommandHandler(ctx);

        var result = await handler.Handle(new UpdateGroupCommand(sys.Id, "   ", null, ActorId), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("SYSTEM_GROUP_LOCKED", result.ErrorCode);
    }
}
