using System.Security.Claims;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using aspire_react.Server.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// Unit tests for Component business rules: Category (required on create, delete-guard, type filter),
/// Company scoping, update whitelist / FIELD_LOCKED, and the allocation-history delete guard.
/// Validations run BEFORE the transaction is opened, so they are testable with InMemory.
/// </summary>
public class CategoryAndComponentTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private sealed class SuperUserScope : ICompanyScopeService
    {
        public bool IsSuperUser() => true;
        public Task<List<Guid>> GetUserCompanyIdsAsync() => Task.FromResult(new List<Guid>());
        public Task<Guid?> GetCurrentUserCompanyIdAsync() => Task.FromResult<Guid?>(null);
        public Task<bool> IsCompanyIdInUserScopeAsync(Guid companyId) => Task.FromResult(true);
    }

    private static AppDbContext CreateContext(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(options, new SuperUserScope());
    }

    private static T WithUser<T>(T controller, Guid userId) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim("sub", userId.ToString())
                }, "test"))
            }
        };
        return controller;
    }

    // Anonymous types are internal to the Server assembly â€” dynamic binding can't read their
    // members cross-assembly, so round-trip through JSON instead. Use Web defaults (camelCase)
    // to match the real API serialization contract.
    private static readonly System.Text.Json.JsonSerializerOptions WebJson = new(System.Text.Json.JsonSerializerDefaults.Web);

    private static string ReadErrorCode(object? value)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(value, WebJson));
        return doc.RootElement.GetProperty("error_code").GetString()!;
    }

    private static List<string> ReadNames(object? value)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(value, WebJson));
        var names = new List<string>();
        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
            names.Add(item.GetProperty("name").GetString()!);
        return names;
    }

    [Fact]
    public async Task CreateComponent_WithoutCategory_RejectedWithCategoryRequired()
    {
        await using var ctx = CreateContext(nameof(CreateComponent_WithoutCategory_RejectedWithCategoryRequired));
        var controller = WithUser(new ComponentsController(ctx, new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx)), new SuperUserScope(), TestHelpers.CreateActionLogService(ctx)), UserId);

        var request = new CreateComponentRequest(
            Name: "RAM 16GB", Serial: null, Qty: 5, MinAmt: 1,
            CategoryId: null, LocationId: null, CompanyId: null,
            SupplierId: null, ManufacturerId: null, ModelNumber: null,
            OrderNumber: null, PurchaseCost: null, PurchaseDate: null, Notes: null);

        var result = await controller.Create(request);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("CATEGORY_REQUIRED", ReadErrorCode(bad.Value));
        Assert.Empty(await ctx.Components.ToListAsync());
    }

    [Fact]
    public async Task CreateComponent_WithNonComponentCategory_Rejected()
    {
        await using var ctx = CreateContext(nameof(CreateComponent_WithNonComponentCategory_Rejected));
        var assetCategory = new Category { Name = "Laptop", CategoryType = CategoryType.Asset };
        ctx.Categories.Add(assetCategory);
        await ctx.SaveChangesAsync();

        var controller = WithUser(new ComponentsController(ctx, new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx)), new SuperUserScope(), TestHelpers.CreateActionLogService(ctx)), UserId);
        var request = new CreateComponentRequest(
            Name: "RAM 16GB", Serial: null, Qty: 5, MinAmt: 1,
            CategoryId: assetCategory.Id, LocationId: null, CompanyId: null,
            SupplierId: null, ManufacturerId: null, ModelNumber: null,
            OrderNumber: null, PurchaseCost: null, PurchaseDate: null, Notes: null);

        var result = await controller.Create(request);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("INVALID_CATEGORY", ReadErrorCode(bad.Value));
    }

    [Fact]
    public async Task DeleteCategory_InUseByComponent_RejectedWithCategoryInUse()
    {
        await using var ctx = CreateContext(nameof(DeleteCategory_InUseByComponent_RejectedWithCategoryInUse));
        var category = new Category { Name = "RAM", CategoryType = CategoryType.Component };
        ctx.Categories.Add(category);
        ctx.Components.Add(new Component { Name = "RAM 16GB", TrackingType = TrackingType.Bulk, Qty = 5, CategoryId = category.Id });
        await ctx.SaveChangesAsync();

        var controller = WithUser(new AdminController(ctx, new SuperUserScope(), new TestHelpers.NullCacheInvalidator(), TestHelpers.CreateActionLogService(ctx)), UserId);
        var result = await controller.DeleteCategory(category.Id);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("CATEGORY_IN_USE", ReadErrorCode(bad.Value));
        // Category must still exist (delete was rejected).
        Assert.NotNull(await ctx.Categories.FindAsync(category.Id));
    }

    [Fact]
    public async Task DeleteCategory_Unused_Succeeds_AndLogsDelete()
    {
        await using var ctx = CreateContext(nameof(DeleteCategory_Unused_Succeeds_AndLogsDelete));
        var category = new Category { Name = "RAM", CategoryType = CategoryType.Component };
        ctx.Categories.Add(category);
        await ctx.SaveChangesAsync();

        var controller = WithUser(new AdminController(ctx, new SuperUserScope(), new TestHelpers.NullCacheInvalidator(), TestHelpers.CreateActionLogService(ctx)), UserId);
        var result = await controller.DeleteCategory(category.Id);

        Assert.IsType<OkObjectResult>(result);
        Assert.Null(await ctx.Categories.FindAsync(category.Id));
        Assert.Equal(1, await ctx.ActionLogs.CountAsync(l => l.ItemType == ItemType.Category && l.ActionType == ActionType.Delete));
    }

    [Fact]
    public async Task GetCategories_ByTypeComponent_ReturnsOnlyComponentCategories()
    {
        await using var ctx = CreateContext(nameof(GetCategories_ByTypeComponent_ReturnsOnlyComponentCategories));
        ctx.Categories.AddRange(
            new Category { Name = "RAM", CategoryType = CategoryType.Component },
            new Category { Name = "á»” cá»©ng", CategoryType = CategoryType.Component },
            new Category { Name = "Laptop", CategoryType = CategoryType.Asset });
        await ctx.SaveChangesAsync();

        var controller = WithUser(new AdminController(ctx, new SuperUserScope(), new TestHelpers.NullCacheInvalidator(), TestHelpers.CreateActionLogService(ctx)), UserId);
        var result = await controller.GetCategories(CategoryType.Component);

        var ok = Assert.IsType<OkObjectResult>(result);
        var names = ReadNames(ok.Value);
        Assert.Equal(2, names.Count);
        Assert.DoesNotContain("Laptop", names);
        Assert.Contains("RAM", names);
    }

    // ==================== Company/Location scoping ====================

    [Fact]
    public async Task CreateComponent_WithoutCompany_RejectedWithCompanyRequired()
    {
        await using var ctx = CreateContext(nameof(CreateComponent_WithoutCompany_RejectedWithCompanyRequired));
        var category = new Category { Name = "RAM", CategoryType = CategoryType.Component };
        ctx.Categories.Add(category);
        await ctx.SaveChangesAsync();

        var controller = WithUser(new ComponentsController(ctx, new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx)), new SuperUserScope(), TestHelpers.CreateActionLogService(ctx)), UserId);
        var request = new CreateComponentRequest(
            Name: "RAM 16GB", Serial: null, Qty: 5, MinAmt: 1,
            CategoryId: category.Id, LocationId: null, CompanyId: null,
            SupplierId: null, ManufacturerId: null, ModelNumber: null,
            OrderNumber: null, PurchaseCost: null, PurchaseDate: null, Notes: null);

        var result = await controller.Create(request);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("COMPANY_REQUIRED", ReadErrorCode(bad.Value));
    }

    [Fact]
    public async Task CreateComponent_WithInvalidCompany_Rejected()
    {
        await using var ctx = CreateContext(nameof(CreateComponent_WithInvalidCompany_Rejected));
        var category = new Category { Name = "RAM", CategoryType = CategoryType.Component };
        ctx.Categories.Add(category);
        await ctx.SaveChangesAsync();

        var controller = WithUser(new ComponentsController(ctx, new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx)), new SuperUserScope(), TestHelpers.CreateActionLogService(ctx)), UserId);
        var request = new CreateComponentRequest(
            Name: "RAM 16GB", Serial: null, Qty: 5, MinAmt: 1,
            CategoryId: category.Id, LocationId: null, CompanyId: Guid.NewGuid(),
            SupplierId: null, ManufacturerId: null, ModelNumber: null,
            OrderNumber: null, PurchaseCost: null, PurchaseDate: null, Notes: null);

        var result = await controller.Create(request);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("INVALID_COMPANY", ReadErrorCode(bad.Value));
    }

    // ==================== Update whitelist / FIELD_LOCKED ====================

    [Fact]
    public async Task UpdateComponent_LockedCategory_RejectedWithFieldLocked()
    {
        await using var ctx = CreateContext(nameof(UpdateComponent_LockedCategory_RejectedWithFieldLocked));
        var category = new Category { Name = "RAM", CategoryType = CategoryType.Component };
        var company = new Company { Name = "CT-A" };
        ctx.Categories.Add(category);
        ctx.Companies.Add(company);
        var component = new Component { Name = "RAM 16GB", TrackingType = TrackingType.Bulk, Qty = 5, CategoryId = category.Id, CompanyId = company.Id };
        ctx.Components.Add(component);
        await ctx.SaveChangesAsync();

        var otherCategory = new Category { Name = "á»” cá»©ng", CategoryType = CategoryType.Component };
        ctx.Categories.Add(otherCategory);
        await ctx.SaveChangesAsync();

        var controller = WithUser(new ComponentsController(ctx, new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx)), new SuperUserScope(), TestHelpers.CreateActionLogService(ctx)), UserId);
        // Client tries to change the CategoryId â†’ FIELD_LOCKED.
        var result = await controller.Update(component.Id, new UpdateComponentRequest(Name: "Äá»•i tÃªn", CategoryId: otherCategory.Id));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("FIELD_LOCKED", ReadErrorCode(bad.Value));
    }

    [Fact]
    public async Task UpdateComponent_AllowsWhitelist_AndIgnoresQty()
    {
        await using var ctx = CreateContext(nameof(UpdateComponent_AllowsWhitelist_AndIgnoresQty));
        var category = new Category { Name = "RAM", CategoryType = CategoryType.Component };
        var company = new Company { Name = "CT-A" };
        ctx.Categories.Add(category);
        ctx.Companies.Add(company);
        var component = new Component { Name = "RAM 16GB", TrackingType = TrackingType.Bulk, Qty = 5, MinAmt = 1, CategoryId = category.Id, CompanyId = company.Id };
        ctx.Components.Add(component);
        await ctx.SaveChangesAsync();

        var controller = WithUser(new ComponentsController(ctx, new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx)), new SuperUserScope(), TestHelpers.CreateActionLogService(ctx)), UserId);
        // Same CategoryId/CompanyId as current â†’ allowed; Qty is always ignored.
        var result = await controller.Update(component.Id, new UpdateComponentRequest(
            Name: "RAM 32GB", MinAmt: 2, CategoryId: category.Id, CompanyId: company.Id, Qty: 999));

        Assert.IsType<OkObjectResult>(result);
        var updated = await ctx.Components.FindAsync(component.Id);
        Assert.NotNull(updated);
        Assert.Equal("RAM 32GB", updated!.Name);
        Assert.Equal(2, updated.MinAmt);
        Assert.Equal(5, updated.Qty); // Qty ignored â€” never editable
    }

    // ==================== Delete guard (allocation history) ====================

    [Fact]
    public async Task DeleteComponent_WithAllocationHistory_Rejected()
    {
        await using var ctx = CreateContext(nameof(DeleteComponent_WithAllocationHistory_Rejected));
        var category = new Category { Name = "RAM", CategoryType = CategoryType.Component };
        var company = new Company { Name = "CT-A" };
        ctx.Categories.Add(category);
        ctx.Companies.Add(company);
        var component = new Component { Name = "RAM 16GB", TrackingType = TrackingType.Bulk, Qty = 5, CategoryId = category.Id, CompanyId = company.Id };
        ctx.Components.Add(component);
        ctx.ActionLogs.Add(new ActionLog { ItemType = ItemType.Component, ItemId = component.Id, ActionType = ActionType.Checkout, CreatedBy = UserId });
        await ctx.SaveChangesAsync();

        var controller = WithUser(new ComponentsController(ctx, new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx)), new SuperUserScope(), TestHelpers.CreateActionLogService(ctx)), UserId);
        var result = await controller.Delete(component.Id);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("COMPONENT_HAS_ALLOCATION_HISTORY", ReadErrorCode(bad.Value));
        Assert.NotNull(await ctx.Components.FindAsync(component.Id)); // not deleted
    }

    [Fact]
    public async Task DeleteComponent_WithoutAllocationHistory_Succeeds_AndLogsDelete()
    {
        await using var ctx = CreateContext(nameof(DeleteComponent_WithoutAllocationHistory_Succeeds_AndLogsDelete));
        var category = new Category { Name = "RAM", CategoryType = CategoryType.Component };
        var company = new Company { Name = "CT-A" };
        ctx.Categories.Add(category);
        ctx.Companies.Add(company);
        var component = new Component { Name = "RAM 16GB", TrackingType = TrackingType.Bulk, Qty = 5, CategoryId = category.Id, CompanyId = company.Id };
        ctx.Components.Add(component);
        await ctx.SaveChangesAsync();

        var controller = WithUser(new ComponentsController(ctx, new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx)), new SuperUserScope(), TestHelpers.CreateActionLogService(ctx)), UserId);
        var result = await controller.Delete(component.Id);

        Assert.IsType<OkObjectResult>(result);
        Assert.Null(await ctx.Components.FindAsync(component.Id));
        Assert.Equal(1, await ctx.ActionLogs.CountAsync(l => l.ItemType == ItemType.Component && l.ActionType == ActionType.Delete));
    }

    // ==================== uncompanied filter ====================

    [Fact]
    public async Task GetComponents_Uncompanied_ReturnsOnlyNullCompanyComponents()
    {
        await using var ctx = CreateContext(nameof(GetComponents_Uncompanied_ReturnsOnlyNullCompanyComponents));
        var company = new Company { Name = "CT-A" };
        var category = new Category { Name = "RAM", CategoryType = CategoryType.Component };
        ctx.Companies.Add(company);
        ctx.Categories.Add(category);
        ctx.Components.AddRange(
            new Component { Name = "CÃ“ cÃ´ng ty", TrackingType = TrackingType.Bulk, Qty = 1, CategoryId = category.Id, CompanyId = company.Id },
            new Component { Name = "ChÆ°a xÃ¡c Ä‘á»‹nh", TrackingType = TrackingType.Bulk, Qty = 1, CategoryId = category.Id, CompanyId = null });
        await ctx.SaveChangesAsync();

        var controller = WithUser(new ComponentsController(ctx, new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx)), new SuperUserScope(), TestHelpers.CreateActionLogService(ctx)), UserId);
        var result = await controller.GetComponents(search: null, categoryId: null, companyId: null, locationId: null, uncategorized: false, uncompanied: true, page: 1, pageSize: 20);

        var ok = Assert.IsType<OkObjectResult>(result);
        var names = ReadNames(ok.Value);
        Assert.Single(names);
        Assert.Equal("ChÆ°a xÃ¡c Ä‘á»‹nh", names[0]);
    }
}
