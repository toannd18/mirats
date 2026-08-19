using System.Security.Claims;
using aspire_react.Server.Application.Assets.Commands;
using aspire_react.Server.Application.Users.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
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
/// Task M2 â€” patch-safety for the latent group (User, Asset.Name, Accessory, Admin reference-data):
/// a partial payload (missing a field) must NOT wipe the field back to false/0/empty.
/// </summary>
public class TaskM2PatchSafetyTests
{
    private static AppDbContext CreateContext(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options, new TestHelpers.SuperUserScope());
    }

    private static void AttachUser(ControllerBase c, Guid id)
        => c.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("local_user_id", id.ToString()) }, "Test"))
            }
        };

    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly TestHelpers.FakeScope SuperScope = new() { Super = true };

    // =========================================================================
    // User â€” missing isSuperUser/isActive must NOT strip admin/deactivate
    // =========================================================================

    [Fact]
    public async Task User_Update_WithoutFlags_PreservesSuperUserAndActive()
    {
        await using var ctx = CreateContext(nameof(User_Update_WithoutFlags_PreservesSuperUserAndActive));
        var user = new User
        {
            Username = "admin1",
            Email = "a@l",
            FirstName = "F",
            LastName = "L",
            IsSuperUser = true,
            IsActive = true
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var handler = new UpdateUserCommandHandler(ctx, new TestHelpers.FakeKeycloakService(),
            TestHelpers.CreateActionLogService(ctx, ActorId), NullLogger<UpdateUserCommandHandler>.Instance);

        // Partial payload: no IsSuperUser / IsActive â†’ must keep the existing true/true.
        var result = await handler.Handle(new UpdateUserCommand
        {
            Id = user.Id,
            FirstName = "NewF",
            LastName = "NewL",
            Email = "a@l"
        }, CancellationToken.None);

        Assert.True(result.Success);
        var reloaded = await ctx.Users.SingleAsync(x => x.Id == user.Id);
        Assert.True(reloaded.IsSuperUser); // NOT silently stripped to false
        Assert.True(reloaded.IsActive);    // NOT silently deactivated
    }

    [Fact]
    public async Task User_Update_ExplicitFlag_Applied()
    {
        await using var ctx = CreateContext(nameof(User_Update_ExplicitFlag_Applied));
        var user = new User
        {
            Username = "u1",
            Email = "a@l",
            FirstName = "F",
            LastName = "L",
            IsSuperUser = true,
            IsActive = true
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var handler = new UpdateUserCommandHandler(ctx, new TestHelpers.FakeKeycloakService(),
            TestHelpers.CreateActionLogService(ctx, ActorId), NullLogger<UpdateUserCommandHandler>.Instance);

        var result = await handler.Handle(new UpdateUserCommand
        {
            Id = user.Id,
            FirstName = "F",
            LastName = "L",
            Email = "a@l",
            IsSuperUser = false,
            IsActive = true
        }, CancellationToken.None);

        Assert.True(result.Success);
        var reloaded = await ctx.Users.SingleAsync(x => x.Id == user.Id);
        Assert.False(reloaded.IsSuperUser); // explicit false applied
        Assert.True(reloaded.IsActive);
    }

    // =========================================================================
    // Asset.Name â€” missing name must NOT wipe the existing name
    // =========================================================================

    [Fact]
    public async Task Asset_Update_WithoutName_PreservesName()
    {
        await using var ctx = CreateContext(nameof(Asset_Update_WithoutName_PreservesName));
        var asset = new Asset
        {
            AssetTag = "AST-1",
            Name = "Original Name",
            CompanyId = null
        };
        ctx.Assets.Add(asset);
        await ctx.SaveChangesAsync();

        var handler = new UpdateAssetCommandHandler(ctx, TestHelpers.CreateActionLogService(ctx, ActorId), SuperScope);
        var result = await handler.Handle(new UpdateAssetCommand
        {
            Id = asset.Id,
            AssetTag = "AST-1",
            Name = "",
            Notes = "new note",
            CurrentUserId = ActorId
        }, CancellationToken.None);

        Assert.True(result.Success);
        var reloaded = await ctx.Assets.SingleAsync(x => x.Id == asset.Id);
        Assert.Equal("Original Name", reloaded.Name); // NOT wiped to empty
        Assert.Equal("new note", reloaded.Notes);
    }

    // =========================================================================
    // Accessory â€” patch semantics + CompanyId lock after checkout
    // =========================================================================

    private static AccessoriesController BuildAccessories(AppDbContext ctx)
    {
        var c = new AccessoriesController(ctx, new TestHelpers.ThrowingMediator(), new TestHelpers.FakeCurrentUser(), SuperScope);
        AttachUser(c, ActorId);
        return c;
    }

    [Fact]
    public async Task Accessory_Update_WithoutFields_PreservesOthers()
    {
        await using var ctx = CreateContext(nameof(Accessory_Update_WithoutFields_PreservesOthers));
        var acc = new Accessory
        {
            Name = "Old Acc",
            Qty = 5,
            MinAmt = 1,
            ModelNumber = "M-1",
            Notes = "keep"
        };
        ctx.Accessories.Add(acc);
        await ctx.SaveChangesAsync();

        var controller = BuildAccessories(ctx);
        var result = await controller.Update(acc.Id, new UpdateAccessoryRequest(Name: "New Acc"));
        Assert.IsType<OkObjectResult>(result);

        var reloaded = await ctx.Accessories.SingleAsync(x => x.Id == acc.Id);
        Assert.Equal("New Acc", reloaded.Name);
        Assert.Equal(5, reloaded.Qty);         // NOT reset to 0
        Assert.Equal(1, reloaded.MinAmt);      // NOT reset to 0
        Assert.Equal("M-1", reloaded.ModelNumber); // preserved
        Assert.Equal("keep", reloaded.Notes);      // preserved
    }

    [Fact]
    public async Task Accessory_ChangeCompanyAfterCheckout_FieldLocked()
    {
        await using var ctx = CreateContext(nameof(Accessory_ChangeCompanyAfterCheckout_FieldLocked));
        var co = new Company { Name = "CT-A" };
        ctx.Companies.Add(co);
        await ctx.SaveChangesAsync();
        var acc = new Accessory
        {
            Name = "A",
            Qty = 1,
            MinAmt = 0,
            CompanyId = co.Id
        };
        ctx.Accessories.Add(acc);
        await ctx.SaveChangesAsync();
        ctx.AccessoryCheckouts.Add(new AccessoryCheckout
        {
            AccessoryId = acc.Id,
            CheckoutType = AccessoryCheckoutType.User,
            TargetId = ActorId,
            AssignedQty = 1,
            ReturnedQty = 0
        });
        await ctx.SaveChangesAsync();

        var controller = BuildAccessories(ctx);
        var result = await controller.Update(acc.Id, new UpdateAccessoryRequest(CompanyId: Guid.NewGuid()));
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("FIELD_LOCKED", System.Text.Json.JsonSerializer.Serialize(bad.Value));
    }

    // =========================================================================
    // Admin reference-data â€” patch semantics (Category as representative)
    // =========================================================================

    [Fact]
    public async Task Category_Update_WithoutFields_PreservesOthers()
    {
        await using var ctx = CreateContext(nameof(Category_Update_WithoutFields_PreservesOthers));
        var cat = new Category { Name = "Old Cat", CategoryType = CategoryType.Asset, TagColor = "#ff0000", Notes = "keep-notes" };
        ctx.Categories.Add(cat);
        await ctx.SaveChangesAsync();

        var controller = new AdminController(ctx, SuperScope, new TestHelpers.NullCacheInvalidator(), TestHelpers.CreateActionLogService(ctx));
        AttachUser(controller, ActorId);
        var result = await controller.UpdateCategory(cat.Id, new UpdateCategoryRequest(Name: "New Cat"));
        Assert.IsType<OkObjectResult>(result);

        var reloaded = await ctx.Categories.SingleAsync(x => x.Id == cat.Id);
        Assert.Equal("New Cat", reloaded.Name);
        Assert.Equal("#ff0000", reloaded.TagColor); // preserved
        Assert.Equal("keep-notes", reloaded.Notes); // preserved
    }
}
