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
/// Unit tests for the Consumable checkout business rules (stock decrease, complete ActionLog,
/// company isolation, receiver must be a User) plus the Confirm workflow and CRUD audit logging.
/// Mirrors LicenseTests / ComponentAllocationServiceTests patterns.
/// </summary>
public class ConsumableTests
{
    /// <summary>Superuser scope so the FMCS global query filters short-circuit (see-all).</summary>
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

    private static ActionLogService CreateActionLogService(AppDbContext ctx)
        => new(ctx, new HttpContextAccessor()); // HttpContext null â†’ claim fallback skipped when userId provided

    private static async Task<(Guid companyId, Guid categoryId)> SeedCompanyAndCategoryAsync(AppDbContext ctx)
    {
        var company = new Company { Name = "CÃ´ng ty Test" };
        ctx.Companies.Add(company);
        var category = new Category { Name = "Háº¡t máº¡ng", CategoryType = CategoryType.Consumable };
        ctx.Categories.Add(category);
        await ctx.SaveChangesAsync();
        return (company.Id, category.Id);
    }

    private static async Task<(Guid consumableId, Guid companyId)> SeedConsumableAsync(
        AppDbContext ctx, int qty = 10, Guid? companyId = null, ConsumableStatus? status = null)
    {
        var (defaultCompanyId, categoryId) = await SeedCompanyAndCategoryAsync(ctx);
        var c = new Consumable
        {
            Name = "Háº¡t máº¡ng RJ 45",
            Qty = qty,
            MinAmt = 1,
            CategoryId = categoryId,
            CompanyId = companyId ?? defaultCompanyId,
            Status = status ?? ConsumableStatus.Pending
        };
        ctx.Consumables.Add(c);
        await ctx.SaveChangesAsync();
        return (c.Id, c.CompanyId!.Value);
    }

    private static async Task<Guid> SeedUserAsync(AppDbContext ctx, Guid companyId, string username = "u1")
    {
        var user = new User
        {
            Username = username,
            Email = $"{username}@test.local",
            FirstName = "Nguyá»…n",
            LastName = "VÄƒn A",
            CompanyId = companyId
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<ConsumablesController> CreateControllerAsync(AppDbContext ctx, Guid localUserId)
    {
        var actionLogService = CreateActionLogService(ctx);
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("local_user_id", localUserId.ToString())
            }, "Test"))
        };
        var controller = new ConsumablesController(TestHelpers.BuildMediator(ctx))
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
        return controller;
    }

    // ==================== Checkout (service-level) ====================

    [Fact]
    public async Task Checkout_Succeeds_DecreasesRemaining_AndWritesCompleteActionLog()
    {
        await using var ctx = CreateContext(nameof(Checkout_Succeeds_DecreasesRemaining_AndWritesCompleteActionLog));
        var (consumableId, companyId) = await SeedConsumableAsync(ctx, qty: 10, status: ConsumableStatus.Confirmed);
        var userId = await SeedUserAsync(ctx, companyId);
        var actorId = await SeedUserAsync(ctx, companyId, "admin");
        var service = new ConsumableAllocationService(ctx, CreateActionLogService(ctx), new SuperUserScope());

        var result = await service.CheckoutAsync(consumableId, userId, quantity: 3, note: "cáº¥p cho váº­n hÃ nh", actorId);

        Assert.True(result.Success);
        var checkout = await ctx.ConsumableCheckouts.SingleAsync(ch => ch.ConsumableId == consumableId);
        Assert.Equal(3, checkout.Quantity);
        Assert.Equal(userId, checkout.UserId);

        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.Consumable && l.ActionType == ActionType.Checkout);
        Assert.Equal(ActionType.Checkout, log.ActionType);
        // Complete audit trail â€” the receiving user + company must be recorded (gap Ä‘Ã£ Ä‘Æ°á»£c sá»­a).
        Assert.Equal(AssignmentTargetType.User, log.TargetType);
        Assert.Equal(userId, log.TargetId);
        Assert.Equal(companyId, log.CompanyId);
        Assert.Equal(actorId, log.CreatedBy);
        Assert.Contains("\"quantity\":3", log.LogMeta);

        // Stock is derived: Qty(10) - sum(Quantity)(3) = 7
        var consumable = await ctx.Consumables.Include(c => c.Checkouts).SingleAsync(c => c.Id == consumableId);
        Assert.Equal(7, consumable.Qty - consumable.Checkouts.Sum(ch => ch.Quantity));
    }

    [Fact]
    public async Task Checkout_Pending_Blocked_WithConfirmErrorCode()
    {
        await using var ctx = CreateContext(nameof(Checkout_Pending_Blocked_WithConfirmErrorCode));
        var (consumableId, companyId) = await SeedConsumableAsync(ctx, qty: 10, status: ConsumableStatus.Pending);
        var userId = await SeedUserAsync(ctx, companyId);
        var actorId = await SeedUserAsync(ctx, companyId, "admin");
        var service = new ConsumableAllocationService(ctx, CreateActionLogService(ctx), new SuperUserScope());

        var result = await service.CheckoutAsync(consumableId, userId, quantity: 1, note: null, actorId);

        Assert.False(result.Success);
        Assert.Equal("CONSUMABLE_NOT_CONFIRMED", result.ErrorCode);
        Assert.Empty(await ctx.ConsumableCheckouts.ToListAsync());
        Assert.Empty(await ctx.ActionLogs.ToListAsync());
    }

    [Fact]
    public async Task Checkout_CrossCompany_Blocked_NoCheckoutNoLog()
    {
        await using var ctx = CreateContext(nameof(Checkout_CrossCompany_Blocked_NoCheckoutNoLog));
        var (consumableId, companyId) = await SeedConsumableAsync(ctx, qty: 10, status: ConsumableStatus.Confirmed);
        var otherCompany = new Company { Name = "CÃ´ng ty KhÃ¡c" };
        ctx.Companies.Add(otherCompany);
        await ctx.SaveChangesAsync();
        var foreignUser = await SeedUserAsync(ctx, otherCompany.Id, "foreign");
        var actorId = await SeedUserAsync(ctx, companyId, "admin");
        var service = new ConsumableAllocationService(ctx, CreateActionLogService(ctx), new SuperUserScope());

        var result = await service.CheckoutAsync(consumableId, foreignUser, quantity: 1, note: null, actorId);

        Assert.False(result.Success);
        Assert.Equal("CONSUMABLE_COMPANY_MISMATCH", result.ErrorCode);
        Assert.Empty(await ctx.ConsumableCheckouts.ToListAsync());
        Assert.Empty(await ctx.ActionLogs.ToListAsync());
    }

    [Fact]
    public async Task Checkout_InsufficientStock_Blocked()
    {
        await using var ctx = CreateContext(nameof(Checkout_InsufficientStock_Blocked));
        var (consumableId, companyId) = await SeedConsumableAsync(ctx, qty: 2, status: ConsumableStatus.Confirmed);
        var userId = await SeedUserAsync(ctx, companyId);
        var actorId = await SeedUserAsync(ctx, companyId, "admin");
        var service = new ConsumableAllocationService(ctx, CreateActionLogService(ctx), new SuperUserScope());

        var result = await service.CheckoutAsync(consumableId, userId, quantity: 5, note: null, actorId);

        Assert.False(result.Success);
        Assert.Equal("INSUFFICIENT_STOCK", result.ErrorCode);
        Assert.Empty(await ctx.ConsumableCheckouts.ToListAsync());
        Assert.Empty(await ctx.ActionLogs.ToListAsync());
    }

    [Fact]
    public async Task Checkout_InvalidQuantity_Blocked()
    {
        await using var ctx = CreateContext(nameof(Checkout_InvalidQuantity_Blocked));
        var (consumableId, companyId) = await SeedConsumableAsync(ctx, qty: 10, status: ConsumableStatus.Confirmed);
        var userId = await SeedUserAsync(ctx, companyId);
        var actorId = await SeedUserAsync(ctx, companyId, "admin");
        var service = new ConsumableAllocationService(ctx, CreateActionLogService(ctx), new SuperUserScope());

        var result = await service.CheckoutAsync(consumableId, userId, quantity: 0, note: null, actorId);

        Assert.False(result.Success);
        Assert.Equal("INVALID_QUANTITY", result.ErrorCode);
    }

    [Fact]
    public async Task Checkout_UserNotFound_Blocked()
    {
        await using var ctx = CreateContext(nameof(Checkout_UserNotFound_Blocked));
        var (consumableId, companyId) = await SeedConsumableAsync(ctx, qty: 10, status: ConsumableStatus.Confirmed);
        var actorId = await SeedUserAsync(ctx, companyId, "admin");
        var service = new ConsumableAllocationService(ctx, CreateActionLogService(ctx), new SuperUserScope());

        var result = await service.CheckoutAsync(consumableId, Guid.NewGuid(), quantity: 1, note: null, actorId);

        Assert.False(result.Success);
        Assert.Equal("TARGET_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task Checkout_ConsumableNotFound_Blocked()
    {
        await using var ctx = CreateContext(nameof(Checkout_ConsumableNotFound_Blocked));
        var service = new ConsumableAllocationService(ctx, CreateActionLogService(ctx), new SuperUserScope());

        var result = await service.CheckoutAsync(Guid.NewGuid(), Guid.NewGuid(), quantity: 1, note: null, Guid.NewGuid());

        Assert.False(result.Success);
        Assert.Equal("NOT_FOUND", result.ErrorCode);
    }

    // ==================== Confirm workflow (controller-level) ====================

    [Fact]
    public async Task Confirm_FromPending_SetsStatus_AndLogsConfirm()
    {
        await using var ctx = CreateContext(nameof(Confirm_FromPending_SetsStatus_AndLogsConfirm));
        var (consumableId, companyId) = await SeedConsumableAsync(ctx, qty: 5);
        var adminId = await SeedUserAsync(ctx, companyId, "admin");
        var controller = await CreateControllerAsync(ctx, adminId);

        var result = await controller.Confirm(consumableId);

        Assert.IsType<OkObjectResult>(result);
        var consumable = await ctx.Consumables.SingleAsync(c => c.Id == consumableId);
        Assert.Equal(ConsumableStatus.Confirmed, consumable.Status);
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.Consumable && l.ActionType == ActionType.Confirm);
        Assert.Equal(adminId, log.CreatedBy);
    }

    [Fact]
    public async Task Confirm_AlreadyConfirmed_ReturnsBadRequest()
    {
        await using var ctx = CreateContext(nameof(Confirm_AlreadyConfirmed_ReturnsBadRequest));
        var (consumableId, companyId) = await SeedConsumableAsync(ctx, qty: 5);
        var adminId = await SeedUserAsync(ctx, companyId, "admin");
        var consumable = await ctx.Consumables.SingleAsync(c => c.Id == consumableId);
        consumable.Status = ConsumableStatus.Confirmed;
        await ctx.SaveChangesAsync();
        var controller = await CreateControllerAsync(ctx, adminId);

        var result = await controller.Confirm(consumableId);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await ctx.ActionLogs.ToListAsync());
    }

    // ==================== CRUD audit logging (controller-level) ====================

    [Fact]
    public async Task Create_LogsCreateAction()
    {
        await using var ctx = CreateContext(nameof(Create_LogsCreateAction));
        var (companyId, categoryId) = await SeedCompanyAndCategoryAsync(ctx);
        var adminId = await SeedUserAsync(ctx, companyId, "admin");
        var controller = await CreateControllerAsync(ctx, adminId);

        var result = await controller.Create(new CreateConsumableRequest(
            "Giáº¥y in A4", null, 100, 10,
            CategoryId: categoryId, ManufacturerId: null, SupplierId: null,
            LocationId: null, CompanyId: companyId,
            ModelNumber: null, OrderNumber: null,
            PurchaseCost: null, PurchaseDate: null, Notes: null, Image: null));

        Assert.IsType<CreatedAtActionResult>(result);
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.Consumable && l.ActionType == ActionType.Create);
        Assert.Equal(adminId, log.CreatedBy);
        Assert.Equal(companyId, log.CompanyId);
    }

    [Fact]
    public async Task Delete_Pending_RemovesAndLogsDelete()
    {
        await using var ctx = CreateContext(nameof(Delete_Pending_RemovesAndLogsDelete));
        var (consumableId, companyId) = await SeedConsumableAsync(ctx, qty: 5);
        var adminId = await SeedUserAsync(ctx, companyId, "admin");
        var controller = await CreateControllerAsync(ctx, adminId);

        var result = await controller.Delete(consumableId);

        Assert.IsType<OkObjectResult>(result);
        Assert.Empty(await ctx.Consumables.Where(c => c.Id == consumableId).ToListAsync());
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.Consumable && l.ActionType == ActionType.Delete);
        Assert.Equal(companyId, log.CompanyId);
    }
}
