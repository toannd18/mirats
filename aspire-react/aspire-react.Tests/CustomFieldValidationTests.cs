using aspire_react.Server.Application.CustomFields.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [BUG-I FIX] CustomFields Create/Update validation + patch-safety — behavior changes approved:
///   create: empty name/slug → 400; dup-slug → 400 (message verbatim); valid create succeeds.
///   update: (b) rename onto an EXISTING slug → 400 (was raw 500 — CONFIRMED bug); re-send same
///           slug no-op; (a) patch-safe: sending only {name} keeps slug/format/flags (was FULL-PUT
///           clearing them); (c) blank name/slug when sent → 400; absent name/slug patch-safe.
/// </summary>
public class CustomFieldValidationTests
{
    private static Guid ActorId { get; } = Guid.NewGuid();

    private static async Task<CustomField> SeedFieldAsync(
        aspire_react.Server.Application.Common.Interfaces.IApplicationDbContext ctx, string name, string slug)
    {
        var f = new CustomField { Name = name, Slug = slug, Format = "text" };
        ctx.CustomFields.Add(f);
        await ctx.SaveChangesAsync();
        return f;
    }

    private static CustomFieldResult Run(CreateCustomFieldCommandHandler h, CreateCustomFieldCommand c)
        => h.Handle(c, CancellationToken.None).GetAwaiter().GetResult();

    [Fact]
    public async Task Create_EmptyNameOrSlug_Rejected()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Create_EmptyNameOrSlug_Rejected));
        var handler = new CreateCustomFieldCommandHandler(ctx);

        var noName = Run(handler, new CreateCustomFieldCommand("  ", "slug-a", "text", null, null, false, null, false, ActorId));
        Assert.False(noName.Success);
        Assert.Equal("Field name is required.", noName.Message);

        var noSlug = Run(handler, new CreateCustomFieldCommand("Field A", " ", "text", null, null, false, null, false, ActorId));
        Assert.False(noSlug.Success);
        Assert.Equal("Field slug is required.", noSlug.Message);

        Assert.Equal(0, await ctx.CustomFields.CountAsync());
    }

    [Fact]
    public async Task Create_DuplicateSlug_Rejected_ValidSucceeds()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Create_DuplicateSlug_Rejected_ValidSucceeds));
        await SeedFieldAsync(ctx, "Field A", "slug-a");
        var handler = new CreateCustomFieldCommandHandler(ctx);

        var dup = Run(handler, new CreateCustomFieldCommand("Field B", "slug-a", "text", null, null, false, null, false, ActorId));
        Assert.False(dup.Success);
        Assert.Equal("A field with this slug already exists.", dup.Message);

        var ok = Run(handler, new CreateCustomFieldCommand("Field B", "slug-b", "text", null, null, false, null, false, ActorId));
        Assert.True(ok.Success);
        Assert.Equal(2, await ctx.CustomFields.CountAsync());
    }

    [Fact]
    public async Task Update_RenameOntoExistingSlug_Rejected_NotRaw500_BugRepro()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Update_RenameOntoExistingSlug_Rejected_NotRaw500_BugRepro));
        var f1 = await SeedFieldAsync(ctx, "Field A", "slug-a");
        await SeedFieldAsync(ctx, "Field B", "slug-b");
        var handler = new UpdateCustomFieldCommandHandler(ctx);

        // BUG-I #2 reproduction: rename f1's slug onto f2's — previously DB unique violation → raw 500.
        var result = await handler.Handle(
            new UpdateCustomFieldCommand(f1.Id, null, "slug-b", null, null, null, null, null, null, ActorId), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("A field with this slug already exists.", result.Message);
        Assert.Equal("slug-a", (await ctx.CustomFields.SingleAsync(x => x.Id == f1.Id)).Slug); // unchanged
    }

    [Fact]
    public async Task Update_PatchSafe_NameOnly_KeepsEverythingElse_BugRepro()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Update_PatchSafe_NameOnly_KeepsEverythingElse_BugRepro));
        var f = await SeedFieldAsync(ctx, "Field A", "slug-a");
        var handler = new UpdateCustomFieldCommandHandler(ctx);

        // BUG-I #1 reproduction: payload with ONLY the name — previously cleared
        // Slug/Format/FieldValues/HelpText/... (FULL-PUT ×8).
        var result = await handler.Handle(
            new UpdateCustomFieldCommand(f.Id, "Field A v2", null, null, null, null, null, null, null, ActorId), CancellationToken.None);

        Assert.True(result.Success);
        var reloaded = await ctx.CustomFields.SingleAsync(x => x.Id == f.Id);
        Assert.Equal("Field A v2", reloaded.Name);
        Assert.Equal("slug-a", reloaded.Slug);   // was cleared before the fix
        Assert.Equal("text", reloaded.Format);   // was cleared before the fix
    }

    [Fact]
    public async Task Update_BlankWhenSent_Rejected_ResendSameSlug_NoOp()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Update_BlankWhenSent_Rejected_ResendSameSlug_NoOp));
        var f = await SeedFieldAsync(ctx, "Field A", "slug-a");
        var handler = new UpdateCustomFieldCommandHandler(ctx);

        var blankSlug = await handler.Handle(
            new UpdateCustomFieldCommand(f.Id, null, "  ", null, null, null, null, null, null, ActorId), CancellationToken.None);
        Assert.False(blankSlug.Success);
        Assert.Equal("Field slug is required.", blankSlug.Message);

        // Re-sending the CURRENT slug must not trip the dup-check (no actual change).
        var noop = await handler.Handle(
            new UpdateCustomFieldCommand(f.Id, "Field A", "slug-a", null, null, null, null, null, null, ActorId), CancellationToken.None);
        Assert.True(noop.Success);
    }
}
