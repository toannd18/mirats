using System.Security.Claims;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using aspire_react.Server.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// Task M1 — patch-safety for Component/License/Consumable Update handlers (mirrors Task F Asset):
/// a partial payload (e.g. only Name) must NOT wipe the other fields back to null/0; field-locks
/// (CompanyId/CategoryId) still work; Create is unaffected.
/// </summary>
public class TaskM1PatchSafetyTests
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
    // Component.Update — partial payload preserves absent fields
    // =========================================================================

    [Fact]
    public async Task Component_PartialUpdate_OnlyName_PreservesOtherFields()
    {
        await using var ctx = CreateContext(nameof(Component_PartialUpdate_OnlyName_PreservesOtherFields));
        var s = new Supplier { Name = "Sup-1" };
        var m = new Manufacturer { Name = "Man-1" };
        ctx.Suppliers.Add(s); ctx.Manufacturers.Add(m); await ctx.SaveChangesAsync();
        var loc = new Location { Name = "Loc-1" };
        ctx.Locations.Add(loc); await ctx.SaveChangesAsync();
        var component = new Component
        {
            Name = "Old Name",
            TrackingType = TrackingType.Bulk,
            Qty = 5,
            MinAmt = 2,
            SupplierId = s.Id,
            ManufacturerId = m.Id,
            ModelNumber = "MOD-1",
            LocationId = loc.Id,
            OrderNumber = "ORD-1",
            PurchaseCost = 100m,
            PurchaseDate = new DateTime(2024, 1, 1)
        };
        ctx.Components.Add(component); await ctx.SaveChangesAsync();

        var controller = new ComponentsController(TestHelpers.BuildMediator(ctx, SuperScope, ActorId));
        AttachUser(controller, ActorId);

        var result = await controller.Update(component.Id, new UpdateComponentRequest(Name: "New Name"));
        Assert.IsType<OkObjectResult>(result);

        var reloaded = await ctx.Components.SingleAsync(x => x.Id == component.Id);
        Assert.Equal("New Name", reloaded.Name);
        Assert.Equal(s.Id, reloaded.SupplierId);          // preserved
        Assert.Equal(m.Id, reloaded.ManufacturerId);      // preserved
        Assert.Equal("MOD-1", reloaded.ModelNumber);      // preserved
        Assert.Equal(loc.Id, reloaded.LocationId);        // preserved
        Assert.Equal("ORD-1", reloaded.OrderNumber);      // preserved
        Assert.Equal(100m, reloaded.PurchaseCost);        // preserved
        Assert.Equal(new DateTime(2024, 1, 1), reloaded.PurchaseDate); // preserved
        Assert.Equal(5, reloaded.Qty);                    // preserved
        Assert.Equal(2, reloaded.MinAmt);                 // preserved
    }

    [Fact]
    public async Task Component_PartialUpdate_ExplicitNullDoesNotWipe()
    {
        await using var ctx = CreateContext(nameof(Component_PartialUpdate_ExplicitNullDoesNotWipe));
        var component = new Component
        {
            Name = "C",
            TrackingType = TrackingType.Bulk,
            Qty = 3,
            MinAmt = 1,
            OrderNumber = "ORD-9"
        };
        ctx.Components.Add(component); await ctx.SaveChangesAsync();

        var controller = new ComponentsController(TestHelpers.BuildMediator(ctx, SuperScope, ActorId));
        AttachUser(controller, ActorId);

        // Payload only carries Name (SupplierId/OrderNumber etc. absent → serialized null).
        await controller.Update(component.Id, new UpdateComponentRequest(Name: "New"));

        var reloaded = await ctx.Components.SingleAsync(x => x.Id == component.Id);
        Assert.Equal("ORD-9", reloaded.OrderNumber); // not wiped
    }

    // =========================================================================
    // License.Update — partial payload preserves absent fields
    // =========================================================================

    private static async Task<License> SeedLicenseAsync(AppDbContext ctx)
    {
        var s = new Supplier { Name = "Sup-L" };
        var m = new Manufacturer { Name = "Man-L" };
        ctx.Suppliers.Add(s); ctx.Manufacturers.Add(m); await ctx.SaveChangesAsync();
        var cat = new Category { Name = "Soft", CategoryType = CategoryType.License };
        ctx.Categories.Add(cat); await ctx.SaveChangesAsync();
        var l = new License
        {
            Name = "Old License",
            Seats = 2,
            SupplierId = s.Id,
            ManufacturerId = m.Id,
            ExpirationDate = new DateTime(2026, 12, 31),
            PurchaseCost = 999m,
            PurchaseDate = new DateTime(2025, 1, 1),
            OrderNumber = "LO-1",
            CategoryId = cat.Id
        };
        ctx.Licenses.Add(l);
        ctx.LicenseSeats.Add(new LicenseSeat { LicenseId = l.Id, SeatNumber = 1 });
        ctx.LicenseSeats.Add(new LicenseSeat { LicenseId = l.Id, SeatNumber = 2 });
        await ctx.SaveChangesAsync();
        return l;
    }

    [Fact]
    public async Task License_PartialUpdate_OnlyName_PreservesOtherFields()
    {
        await using var ctx = CreateContext(nameof(License_PartialUpdate_OnlyName_PreservesOtherFields));
        var l = await SeedLicenseAsync(ctx);
        var controller = new LicensesController(ctx, new TestHelpers.FakeCurrentUser(), SuperScope, TestHelpers.CreateActionLogService(ctx));
        AttachUser(controller, ActorId);

        var result = await controller.Update(l.Id, new UpdateLicenseRequest(Name: "New License"));
        Assert.IsType<OkObjectResult>(result);

        var reloaded = await ctx.Licenses.SingleAsync(x => x.Id == l.Id);
        Assert.Equal("New License", reloaded.Name);
        Assert.Equal(l.SupplierId, reloaded.SupplierId);           // preserved
        Assert.Equal(l.ManufacturerId, reloaded.ManufacturerId);   // preserved
        Assert.Equal(new DateTime(2026, 12, 31), reloaded.ExpirationDate); // preserved
        Assert.Equal(999m, reloaded.PurchaseCost);                 // preserved
        Assert.Equal(new DateTime(2025, 1, 1), reloaded.PurchaseDate);    // preserved
        Assert.Equal("LO-1", reloaded.OrderNumber);                // preserved
        Assert.Equal(2, reloaded.Seats);                           // preserved
    }

    [Fact]
    public async Task License_ChangeCompany_FieldLocked()
    {
        await using var ctx = CreateContext(nameof(License_ChangeCompany_FieldLocked));
        var l = await SeedLicenseAsync(ctx);
        var controller = new LicensesController(ctx, new TestHelpers.FakeCurrentUser(), SuperScope, TestHelpers.CreateActionLogService(ctx));
        AttachUser(controller, ActorId);

        var result = await controller.Update(l.Id, new UpdateLicenseRequest(CompanyId: Guid.NewGuid()));
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("FIELD_LOCKED", System.Text.Json.JsonSerializer.Serialize(bad.Value));
    }

    // =========================================================================
    // Consumable.Update — partial payload preserves absent fields; company lock
    // =========================================================================

    private static ConsumablesController BuildConsumables(AppDbContext ctx)
    {
        var actionLog = TestHelpers.CreateActionLogService(ctx, ActorId);
        var c = new ConsumablesController(ctx, actionLog, new ConsumableAllocationService(ctx, actionLog, SuperScope), SuperScope);
        AttachUser(c, ActorId);
        return c;
    }

    [Fact]
    public async Task Consumable_PartialUpdate_OnlyName_PreservesOtherFields()
    {
        await using var ctx = CreateContext(nameof(Consumable_PartialUpdate_OnlyName_PreservesOtherFields));
        var s = new Supplier { Name = "Sup-C" };
        ctx.Suppliers.Add(s); await ctx.SaveChangesAsync();
        var consumable = new Consumable
        {
            Name = "Old Cons",
            Qty = 10,
            MinAmt = 2,
            SupplierId = s.Id,
            Notes = "note-keep",
            OrderNumber = "CO-1",
            PurchaseCost = 50m
        };
        ctx.Consumables.Add(consumable); await ctx.SaveChangesAsync();

        var controller = BuildConsumables(ctx);
        var result = await controller.Update(consumable.Id, new UpdateConsumableRequest(Name: "New Cons"));
        Assert.IsType<OkObjectResult>(result);

        var reloaded = await ctx.Consumables.SingleAsync(x => x.Id == consumable.Id);
        Assert.Equal("New Cons", reloaded.Name);
        Assert.Equal(10, reloaded.Qty);           // preserved (not reset to 0)
        Assert.Equal(2, reloaded.MinAmt);         // preserved (not reset to 0)
        Assert.Equal(s.Id, reloaded.SupplierId);  // preserved
        Assert.Equal("note-keep", reloaded.Notes);
        Assert.Equal("CO-1", reloaded.OrderNumber);
        Assert.Equal(50m, reloaded.PurchaseCost);
    }

    [Fact]
    public async Task Consumable_ChangeCompanyAfterCheckout_FieldLocked()
    {
        await using var ctx = CreateContext(nameof(Consumable_ChangeCompanyAfterCheckout_FieldLocked));
        var (cA, _) = await SeedCompaniesAsync(ctx);
        var consumable = new Consumable
        {
            Name = "C",
            Qty = 1,
            MinAmt = 0,
            CompanyId = cA
        };
        ctx.Consumables.Add(consumable); await ctx.SaveChangesAsync();
        // simulate a past checkout so the company field is locked
        ctx.ConsumableCheckouts.Add(new ConsumableCheckout
        {
            ConsumableId = consumable.Id,
            UserId = ActorId,
            CreatedByUserId = ActorId,
            Quantity = 1
        });
        await ctx.SaveChangesAsync();

        var controller = BuildConsumables(ctx);
        var result = await controller.Update(consumable.Id, new UpdateConsumableRequest(CompanyId: Guid.NewGuid()));
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("FIELD_LOCKED", System.Text.Json.JsonSerializer.Serialize(bad.Value));
    }

    [Fact]
    public async Task Consumable_ChangeCompanySameValueAfterCheckout_Allowed()
    {
        await using var ctx = CreateContext(nameof(Consumable_ChangeCompanySameValueAfterCheckout_Allowed));
        var (cA, _) = await SeedCompaniesAsync(ctx);
        var consumable = new Consumable
        {
            Name = "C",
            Qty = 1,
            MinAmt = 0,
            CompanyId = cA
        };
        ctx.Consumables.Add(consumable); await ctx.SaveChangesAsync();
        ctx.ConsumableCheckouts.Add(new ConsumableCheckout
        {
            ConsumableId = consumable.Id,
            UserId = ActorId,
            CreatedByUserId = ActorId,
            Quantity = 1
        });
        await ctx.SaveChangesAsync();

        var controller = BuildConsumables(ctx);
        var result = await controller.Update(consumable.Id, new UpdateConsumableRequest(CompanyId: cA));
        Assert.IsType<OkObjectResult>(result); // same value → not a change → allowed
    }

    private static async Task<(Guid cA, Guid cB)> SeedCompaniesAsync(AppDbContext ctx)
    {
        var a = new Company { Name = "CT-A" };
        var b = new Company { Name = "CT-B" };
        ctx.Companies.AddRange(a, b); await ctx.SaveChangesAsync();
        return (a.Id, b.Id);
    }
}
