using aspire_react.Server.Application.Models.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [BUG-H FIX] AssetModel Create/Update validation — behavior change approved:
///   create: empty-name → 400; dup-name → 400; bogus FK → RESOURCE_NOT_FOUND (was raw 500);
///   happy create with valid FKs succeeds;
///   update: blank-when-sent name → 400; rename to existing name → 400; re-send current name no-op;
///   bogus FK on update → RESOURCE_NOT_FOUND; absent fields still patch-safe (keep current).
/// </summary>
public class ModelValidationTests
{
    private static Guid ActorId { get; } = Guid.NewGuid();

    private static async Task<(Guid manufacturerId, Guid categoryId)> SeedRefsAsync(
        aspire_react.Server.Application.Common.Interfaces.IApplicationDbContext ctx)
    {
        var mfr = new Manufacturer { Name = "MFR-A" };
        var cat = new Category { Name = "CAT-A", CategoryType = CategoryType.Asset };
        ctx.Manufacturers.Add(mfr);
        ctx.Categories.Add(cat);
        await ctx.SaveChangesAsync();
        return (mfr.Id, cat.Id);
    }

    [Fact]
    public async Task Create_EmptyName_Rejected()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Create_EmptyName_Rejected));
        var handler = new CreateModelCommandHandler(ctx);

        var result = await handler.Handle(
            new CreateModelCommand("   ", null, null, null, null, null, null, null, false, ActorId), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Tên model không được để trống.", result.Message);
        Assert.Equal(0, await ctx.Models.CountAsync());
    }

    [Fact]
    public async Task Create_DuplicateName_Rejected()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Create_DuplicateName_Rejected));
        ctx.Models.Add(new AssetModel { Name = "Model X" });
        await ctx.SaveChangesAsync();
        var handler = new CreateModelCommandHandler(ctx);

        var result = await handler.Handle(
            new CreateModelCommand("Model X", null, null, null, null, null, null, null, false, ActorId), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Tên model đã tồn tại.", result.Message);
    }

    [Fact]
    public async Task Create_BogusForeignKey_ResourceNotFound_NotRaw500()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Create_BogusForeignKey_ResourceNotFound_NotRaw500));
        var handler = new CreateModelCommandHandler(ctx);

        var result = await handler.Handle(
            new CreateModelCommand("Model OK", null, Guid.NewGuid(), null, null, null, null, null, false, ActorId), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("RESOURCE_NOT_FOUND", result.ErrorCode);
        Assert.Equal(0, await ctx.Models.CountAsync());
    }

    [Fact]
    public async Task Create_ValidRefs_Succeeds()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Create_ValidRefs_Succeeds));
        var (mfrId, catId) = await SeedRefsAsync(ctx);
        var handler = new CreateModelCommandHandler(ctx);

        var result = await handler.Handle(
            new CreateModelCommand("Model OK", "MX-1", mfrId, catId, null, null, null, null, false, ActorId), CancellationToken.None);

        Assert.True(result.Success);
        var m = await ctx.Models.SingleAsync(x => x.Name == "Model OK");
        Assert.Equal(mfrId, m.ManufacturerId);
        Assert.Equal(catId, m.CategoryId);
    }

    [Fact]
    public async Task Update_BlankNameSent_Rejected_AbsentName_PatchSafe()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Update_BlankNameSent_Rejected_AbsentName_PatchSafe));
        var m = new AssetModel { Name = "Model X" };
        ctx.Models.Add(m);
        await ctx.SaveChangesAsync();
        var handler = new UpdateModelCommandHandler(ctx);

        var blank = await handler.Handle(
            new UpdateModelCommand(m.Id, "  ", null, null, null, null, null, null, null, null, ActorId), CancellationToken.None);
        Assert.False(blank.Success);
        Assert.Equal("Tên model không được để trống.", blank.Message);

        // Absent name → patch-safe: no error, name kept (with a bogus FK absent too).
        var absent = await handler.Handle(
            new UpdateModelCommand(m.Id, null, "MX-2", null, null, null, null, null, "note", null, ActorId), CancellationToken.None);
        Assert.True(absent.Success);
        Assert.Equal("Model X", (await ctx.Models.SingleAsync(x => x.Id == m.Id)).Name);
        Assert.Equal("MX-2", (await ctx.Models.SingleAsync(x => x.Id == m.Id)).ModelNumber);
    }

    [Fact]
    public async Task Update_RenameToExisting_Rejected_ResendCurrent_NoOp()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Update_RenameToExisting_Rejected_ResendCurrent_NoOp));
        var m1 = new AssetModel { Name = "Model X" };
        var m2 = new AssetModel { Name = "Model Y" };
        ctx.Models.AddRange(m1, m2);
        await ctx.SaveChangesAsync();
        var handler = new UpdateModelCommandHandler(ctx);

        var dup = await handler.Handle(
            new UpdateModelCommand(m1.Id, "Model Y", null, null, null, null, null, null, null, null, ActorId), CancellationToken.None);
        Assert.False(dup.Success);
        Assert.Equal("Tên model đã tồn tại.", dup.Message);

        var noop = await handler.Handle(
            new UpdateModelCommand(m1.Id, "Model X", null, null, null, null, null, null, null, null, ActorId), CancellationToken.None);
        Assert.True(noop.Success);
    }

    [Fact]
    public async Task Update_BogusForeignKey_ResourceNotFound()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Update_BogusForeignKey_ResourceNotFound));
        var m = new AssetModel { Name = "Model X" };
        ctx.Models.Add(m);
        await ctx.SaveChangesAsync();
        var handler = new UpdateModelCommandHandler(ctx);

        var result = await handler.Handle(
            new UpdateModelCommand(m.Id, null, null, Guid.NewGuid(), null, null, null, null, null, null, ActorId), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("RESOURCE_NOT_FOUND", result.ErrorCode);
        Assert.Null((await ctx.Models.SingleAsync(x => x.Id == m.Id)).ManufacturerId); // unchanged
    }
}
