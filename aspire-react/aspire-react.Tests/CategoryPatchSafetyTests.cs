using aspire_react.Server.Application.Categories.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [BUG-F FIX] Category Update duplicate-Name+CategoryType check — behavior change approved
/// (rename-to-duplicate was 2xx before, now 400 with Create's message). Cases:
///   negative (bug repro): rename to an existing Name+CategoryType → rejected, original kept;
///   positive: rename to a free name succeeds; same-type constraint (same name in ANOTHER
///   CategoryType is still allowed — the unique rule is per-type, same as Create);
///   no-op: re-sending the current name does NOT trip the dup-check.
/// </summary>
public class CategoryPatchSafetyTests
{
    private static Guid ActorId { get; } = Guid.NewGuid();

    private static async Task<Category> SeedCategoryAsync(
        aspire_react.Server.Application.Common.Interfaces.IApplicationDbContext ctx, string name, CategoryType type)
    {
        var c = new Category { Name = name, CategoryType = type };
        ctx.Categories.Add(c);
        await ctx.SaveChangesAsync();
        return c;
    }

    [Fact]
    public async Task Update_RenameToExistingNameSameType_Rejected_BugRepro()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Update_RenameToExistingNameSameType_Rejected_BugRepro));
        var target = await SeedCategoryAsync(ctx, "Cat A", CategoryType.Asset);
        await SeedCategoryAsync(ctx, "Cat B", CategoryType.Asset);
        var handler = new UpdateCategoryCommandHandler(ctx);

        // BUG-F reproduction: rename "Cat A" onto "Cat B" — previously 2xx.
        var result = await handler.Handle(
            new UpdateCategoryCommand(target.Id, "Cat B", null, null, null, null, null, ActorId), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Tên danh mục đã tồn tại.", result.Message);
        Assert.Equal("Cat A", (await ctx.Categories.SingleAsync(x => x.Id == target.Id)).Name); // unchanged
    }

    [Fact]
    public async Task Update_RenameToFreeName_Succeeds()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Update_RenameToFreeName_Succeeds));
        var target = await SeedCategoryAsync(ctx, "Cat A", CategoryType.Asset);
        await SeedCategoryAsync(ctx, "Cat B", CategoryType.Asset);
        var handler = new UpdateCategoryCommandHandler(ctx);

        var result = await handler.Handle(
            new UpdateCategoryCommand(target.Id, "Cat C", null, null, null, null, null, ActorId), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Cat C", (await ctx.Categories.SingleAsync(x => x.Id == target.Id)).Name);
    }

    [Fact]
    public async Task Update_SameNameDifferentType_StillAllowed()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Update_SameNameDifferentType_StillAllowed));
        var target = await SeedCategoryAsync(ctx, "Cat A", CategoryType.Asset);
        await SeedCategoryAsync(ctx, "Cat B", CategoryType.Consumable);
        var handler = new UpdateCategoryCommandHandler(ctx);

        // The unique rule is Name+CategoryType (same as Create): crossing types is not a duplicate.
        var result = await handler.Handle(
            new UpdateCategoryCommand(target.Id, "Cat B", null, null, null, null, null, ActorId), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Cat B", (await ctx.Categories.SingleAsync(x => x.Id == target.Id)).Name);
    }

    [Fact]
    public async Task Update_ResendingCurrentName_NoOp_NotRejected()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Update_ResendingCurrentName_NoOp_NotRejected));
        var target = await SeedCategoryAsync(ctx, "Cat A", CategoryType.Asset);
        var handler = new UpdateCategoryCommandHandler(ctx);

        var result = await handler.Handle(
            new UpdateCategoryCommand(target.Id, "Cat A", null, null, null, null, null, ActorId), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Cat A", (await ctx.Categories.SingleAsync(x => x.Id == target.Id)).Name);
    }
}
