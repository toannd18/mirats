using aspire_react.Server.Application.Accessories.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// ST9/F41 — Accessory module coverage: Create, Checkout (TargetType mapping for all 4 target
/// kinds: User/Department/Location/SystemPosition — the ST4 fix), Company isolation on checkout,
/// Checkin (partial return), and the Delete guard when checkout history exists.
/// Accessory handlers use an ambient transaction which the InMemory provider ignores as a no-op
/// (see TestHelpers.CreateContext), so they run end-to-end under EF InMemory.
/// </summary>
public class AccessoryTests
{
    private static Guid ActorId { get; } = Guid.NewGuid();

    private static async Task<(Guid companyId, Guid categoryId)> SeedCompanyAndCategoryAsync(AppDbContext ctx)
    {
        var company = new Company { Name = "CT-A" };
        ctx.Companies.Add(company);
        var category = new Category { Name = "Phụ kiện", CategoryType = CategoryType.Accessory };
        ctx.Categories.Add(category);
        await ctx.SaveChangesAsync();
        return (company.Id, category.Id);
    }

    private static async Task<Guid> SeedAccessoryAsync(AppDbContext ctx, Guid companyId, int qty = 10)
    {
        var (_, categoryId) = await SeedCompanyAndCategoryAsync(ctx);
        var accessory = new Accessory { Name = "Chuột không dây", Qty = qty, MinAmt = 1, CategoryId = categoryId, CompanyId = companyId };
        ctx.Accessories.Add(accessory);
        await ctx.SaveChangesAsync();
        return accessory.Id;
    }

    private static async Task<Guid> SeedUserAsync(AppDbContext ctx, Guid companyId, string username = "u1")
    {
        var user = new User { Username = username, Email = $"{username}@test.local", FirstName = "A", LastName = "B", CompanyId = companyId };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<Guid> SeedDepartmentAsync(AppDbContext ctx, Guid companyId)
    {
        var dept = new Department { Name = "Phòng IT", CompanyId = companyId };
        ctx.Departments.Add(dept);
        await ctx.SaveChangesAsync();
        return dept.Id;
    }

    private static async Task<Guid> SeedLocationAsync(AppDbContext ctx)
    {
        var location = new Location { Name = "Kho A" };
        ctx.Locations.Add(location);
        await ctx.SaveChangesAsync();
        return location.Id;
    }

    private static async Task<Guid> SeedSystemPositionAsync(AppDbContext ctx, Guid companyId)
    {
        var sysInfo = new SystemInfo { Name = "Hệ thống A", CompanyId = companyId };
        var position = new SystemPosition { Name = "Vị trí 1", SystemInfo = sysInfo };
        ctx.SystemInfos.Add(sysInfo);
        ctx.SystemPositions.Add(position);
        await ctx.SaveChangesAsync();
        return position.Id;
    }

    // ==================== CREATE ====================

    [Fact]
    public async Task Create_Succeeds_CreatesAccessory_AndLogsCreateWithCompanyId()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Create_Succeeds_CreatesAccessory_AndLogsCreateWithCompanyId));
        var (companyId, categoryId) = await SeedCompanyAndCategoryAsync(ctx);
        var actionLog = TestHelpers.CreateActionLogService(ctx, ActorId);
        var handler = new CreateAccessoryCommandHandler(ctx, actionLog, new TestHelpers.SuperUserScope());

        var result = await handler.Handle(new CreateAccessoryCommand
        {
            Name = "Cáp HDMI", Qty = 5, MinAmt = 1, CategoryId = categoryId,
            CompanyId = companyId, CurrentUserId = ActorId
        }, CancellationToken.None);

        Assert.True(result.Success);
        var accessory = await ctx.Accessories.SingleAsync(a => a.Name == "Cáp HDMI");
        Assert.Equal(companyId, accessory.CompanyId);
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.Accessory && l.ActionType == ActionType.Create);
        Assert.Equal(ActorId, log.CreatedBy);
        Assert.Equal(companyId, log.CompanyId);
    }

    // ==================== CHECKOUT — TargetType mapping (ST4 fix) ====================

    [Fact]
    public async Task Checkout_ToUser_LogsTargetTypeUser()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Checkout_ToUser_LogsTargetTypeUser));
        var (companyId, _) = await SeedCompanyAndCategoryAsync(ctx);
        var accessoryId = await SeedAccessoryAsync(ctx, companyId, qty: 10);
        var targetId = await SeedUserAsync(ctx, companyId);
        var handler = new CheckoutAccessoryCommandHandler(ctx, TestHelpers.CreateActionLogService(ctx, ActorId), new TestHelpers.SuperUserScope());

        var result = await handler.Handle(new CheckoutAccessoryCommand
        {
            AccessoryId = accessoryId, CheckoutType = AccessoryCheckoutType.User,
            TargetId = targetId, Quantity = 3, CurrentUserId = ActorId
        }, CancellationToken.None);

        Assert.True(result.Success);
        var checkout = await ctx.AccessoryCheckouts.SingleAsync(c => c.AccessoryId == accessoryId);
        Assert.Equal(targetId, checkout.TargetId);
        Assert.Equal(3, checkout.AssignedQty);

        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.Accessory && l.ActionType == ActionType.Checkout);
        Assert.Equal(AssignmentTargetType.User, log.TargetType);   // ST4: log target must be the REAL checkout target
        Assert.Equal(targetId, log.TargetId);
        Assert.Equal(companyId, log.CompanyId);
        Assert.Equal(ActorId, log.CreatedBy);
    }

    [Fact]
    public async Task Checkout_ToDepartment_LogsTargetTypeDepartment()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Checkout_ToDepartment_LogsTargetTypeDepartment));
        var (companyId, _) = await SeedCompanyAndCategoryAsync(ctx);
        var accessoryId = await SeedAccessoryAsync(ctx, companyId);
        var targetId = await SeedDepartmentAsync(ctx, companyId);
        var handler = new CheckoutAccessoryCommandHandler(ctx, TestHelpers.CreateActionLogService(ctx, ActorId), new TestHelpers.SuperUserScope());

        var result = await handler.Handle(new CheckoutAccessoryCommand
        {
            AccessoryId = accessoryId, CheckoutType = AccessoryCheckoutType.Department,
            TargetId = targetId, Quantity = 2, CurrentUserId = ActorId
        }, CancellationToken.None);

        Assert.True(result.Success);
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.Accessory && l.ActionType == ActionType.Checkout);
        Assert.Equal(AssignmentTargetType.Department, log.TargetType);
        Assert.Equal(targetId, log.TargetId);
    }

    [Fact]
    public async Task Checkout_ToLocation_LogsTargetTypeLocation()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Checkout_ToLocation_LogsTargetTypeLocation));
        var (companyId, _) = await SeedCompanyAndCategoryAsync(ctx);
        var accessoryId = await SeedAccessoryAsync(ctx, companyId);
        var targetId = await SeedLocationAsync(ctx);
        var handler = new CheckoutAccessoryCommandHandler(ctx, TestHelpers.CreateActionLogService(ctx, ActorId), new TestHelpers.SuperUserScope());

        var result = await handler.Handle(new CheckoutAccessoryCommand
        {
            AccessoryId = accessoryId, CheckoutType = AccessoryCheckoutType.Location,
            TargetId = targetId, Quantity = 2, CurrentUserId = ActorId
        }, CancellationToken.None);

        Assert.True(result.Success);
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.Accessory && l.ActionType == ActionType.Checkout);
        Assert.Equal(AssignmentTargetType.Location, log.TargetType); // Location checkout is company-independent (allowed)
        Assert.Equal(targetId, log.TargetId);
    }

    [Fact]
    public async Task Checkout_ToSystemPosition_LogsTargetTypeSystemPosition()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Checkout_ToSystemPosition_LogsTargetTypeSystemPosition));
        var (companyId, _) = await SeedCompanyAndCategoryAsync(ctx);
        var accessoryId = await SeedAccessoryAsync(ctx, companyId);
        var targetId = await SeedSystemPositionAsync(ctx, companyId);
        var handler = new CheckoutAccessoryCommandHandler(ctx, TestHelpers.CreateActionLogService(ctx, ActorId), new TestHelpers.SuperUserScope());

        var result = await handler.Handle(new CheckoutAccessoryCommand
        {
            AccessoryId = accessoryId, CheckoutType = AccessoryCheckoutType.SystemPosition,
            TargetId = targetId, Quantity = 2, CurrentUserId = ActorId
        }, CancellationToken.None);

        Assert.True(result.Success);
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.Accessory && l.ActionType == ActionType.Checkout);
        Assert.Equal(AssignmentTargetType.SystemPosition, log.TargetType);
        Assert.Equal(targetId, log.TargetId);
        Assert.Equal(companyId, log.CompanyId);
    }


    // ==================== CHECKOUT — guards & company isolation ====================

    [Fact]
    public async Task Checkout_CrossCompanyUser_RejectedWithCompanyMismatch()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Checkout_CrossCompanyUser_RejectedWithCompanyMismatch));
        var (companyId, _) = await SeedCompanyAndCategoryAsync(ctx);
        var accessoryId = await SeedAccessoryAsync(ctx, companyId);
        var otherCompany = new Company { Name = "CT-B" };
        ctx.Companies.Add(otherCompany);
        await ctx.SaveChangesAsync();
        var otherUser = new User { Username = "other", Email = "o@t.local", FirstName = "B", LastName = "B", CompanyId = otherCompany.Id };
        ctx.Users.Add(otherUser);
        await ctx.SaveChangesAsync();
        var handler = new CheckoutAccessoryCommandHandler(ctx, TestHelpers.CreateActionLogService(ctx, ActorId), new TestHelpers.SuperUserScope());

        var result = await handler.Handle(new CheckoutAccessoryCommand
        {
            AccessoryId = accessoryId, CheckoutType = AccessoryCheckoutType.User,
            TargetId = otherUser.Id, Quantity = 1, CurrentUserId = ActorId
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("COMPANY_MISMATCH", result.ErrorCode);
        Assert.Empty(await ctx.AccessoryCheckouts.ToListAsync());
    }

    [Fact]
    public async Task Checkout_InsufficientStock_Rejected()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Checkout_InsufficientStock_Rejected));
        var (companyId, _) = await SeedCompanyAndCategoryAsync(ctx);
        var accessoryId = await SeedAccessoryAsync(ctx, companyId, qty: 5);
        var targetId = await SeedUserAsync(ctx, companyId);
        var handler = new CheckoutAccessoryCommandHandler(ctx, TestHelpers.CreateActionLogService(ctx, ActorId), new TestHelpers.SuperUserScope());

        var result = await handler.Handle(new CheckoutAccessoryCommand
        {
            AccessoryId = accessoryId, CheckoutType = AccessoryCheckoutType.User,
            TargetId = targetId, Quantity = 6, CurrentUserId = ActorId
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("INSUFFICIENT_STOCK", result.ErrorCode);
    }

    [Fact]
    public async Task Checkout_UnknownTarget_Rejected()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Checkout_UnknownTarget_Rejected));
        var (companyId, _) = await SeedCompanyAndCategoryAsync(ctx);
        var accessoryId = await SeedAccessoryAsync(ctx, companyId);
        var handler = new CheckoutAccessoryCommandHandler(ctx, TestHelpers.CreateActionLogService(ctx, ActorId), new TestHelpers.SuperUserScope());

        var result = await handler.Handle(new CheckoutAccessoryCommand
        {
            AccessoryId = accessoryId, CheckoutType = AccessoryCheckoutType.User,
            TargetId = Guid.NewGuid(), Quantity = 1, CurrentUserId = ActorId
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("TARGET_NOT_FOUND", result.ErrorCode);
    }


    // ==================== CHECKIN (partial return) ====================

    [Fact]
    public async Task Checkin_PartialReturn_DecreasesRemainingOut_AndLogsCheckin()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Checkin_PartialReturn_DecreasesRemainingOut_AndLogsCheckin));
        var (companyId, _) = await SeedCompanyAndCategoryAsync(ctx);
        var accessoryId = await SeedAccessoryAsync(ctx, companyId, qty: 10);
        var targetId = await SeedUserAsync(ctx, companyId);
        var checkoutHandler = new CheckoutAccessoryCommandHandler(ctx, TestHelpers.CreateActionLogService(ctx, ActorId), new TestHelpers.SuperUserScope());
        await checkoutHandler.Handle(new CheckoutAccessoryCommand
        {
            AccessoryId = accessoryId, CheckoutType = AccessoryCheckoutType.User,
            TargetId = targetId, Quantity = 3, CurrentUserId = ActorId
        }, CancellationToken.None);
        var checkout = await ctx.AccessoryCheckouts.SingleAsync(c => c.AccessoryId == accessoryId);

        var checkinHandler = new CheckinAccessoryCommandHandler(ctx, TestHelpers.CreateActionLogService(ctx, ActorId), new TestHelpers.SuperUserScope());
        var result = await checkinHandler.Handle(new CheckinAccessoryCommand
        {
            CheckoutId = checkout.Id, ReturnQty = 1, Note = "trả 1", CurrentUserId = ActorId
        }, CancellationToken.None);

        Assert.True(result.Success);
        var updated = await ctx.AccessoryCheckouts.SingleAsync(c => c.Id == checkout.Id);
        Assert.Equal(1, updated.ReturnedQty);
        Assert.Equal(2, updated.RemainingCheckedOut);

        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.Accessory && l.ActionType == ActionType.Checkin);
        Assert.Equal(AssignmentTargetType.User, log.TargetType);
        Assert.Equal(targetId, log.TargetId);
        Assert.Equal(companyId, log.CompanyId);
        Assert.Contains("return_qty", log.LogMeta);
        Assert.Contains("changes", log.LogMeta);
    }

    [Fact]
    public async Task Checkin_MoreThanCheckedOut_Rejected()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Checkin_MoreThanCheckedOut_Rejected));
        var (companyId, _) = await SeedCompanyAndCategoryAsync(ctx);
        var accessoryId = await SeedAccessoryAsync(ctx, companyId, qty: 10);
        var targetId = await SeedUserAsync(ctx, companyId);
        var checkoutHandler = new CheckoutAccessoryCommandHandler(ctx, TestHelpers.CreateActionLogService(ctx, ActorId), new TestHelpers.SuperUserScope());
        await checkoutHandler.Handle(new CheckoutAccessoryCommand
        {
            AccessoryId = accessoryId, CheckoutType = AccessoryCheckoutType.User,
            TargetId = targetId, Quantity = 3, CurrentUserId = ActorId
        }, CancellationToken.None);
        var checkout = await ctx.AccessoryCheckouts.SingleAsync(c => c.AccessoryId == accessoryId);

        var checkinHandler = new CheckinAccessoryCommandHandler(ctx, TestHelpers.CreateActionLogService(ctx, ActorId), new TestHelpers.SuperUserScope());
        var result = await checkinHandler.Handle(new CheckinAccessoryCommand
        {
            CheckoutId = checkout.Id, ReturnQty = 4, CurrentUserId = ActorId
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("EXCEEDS_CHECKED_OUT", result.ErrorCode);
    }


    // ==================== DELETE GUARD ====================

    [Fact]
    public async Task Delete_WithCheckoutHistory_Rejected()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Delete_WithCheckoutHistory_Rejected));
        var (companyId, _) = await SeedCompanyAndCategoryAsync(ctx);
        var accessoryId = await SeedAccessoryAsync(ctx, companyId, qty: 10);
        var targetId = await SeedUserAsync(ctx, companyId);
        var checkoutHandler = new CheckoutAccessoryCommandHandler(ctx, TestHelpers.CreateActionLogService(ctx, ActorId), new TestHelpers.SuperUserScope());
        await checkoutHandler.Handle(new CheckoutAccessoryCommand
        {
            AccessoryId = accessoryId, CheckoutType = AccessoryCheckoutType.User,
            TargetId = targetId, Quantity = 1, CurrentUserId = ActorId
        }, CancellationToken.None);

        var deleteHandler = new DeleteAccessoryCommandHandler(ctx, TestHelpers.CreateActionLogService(ctx, ActorId), new TestHelpers.SuperUserScope());
        var result = await deleteHandler.Handle(new DeleteAccessoryCommand { AccessoryId = accessoryId, CurrentUserId = ActorId }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ACCESSORY_HAS_CHECKOUTS", result.ErrorCode);
        Assert.Single(await ctx.Accessories.Where(a => a.Id == accessoryId).ToListAsync()); // still exists
    }

    [Fact]
    public async Task Delete_NoHistory_Succeeds_AndLogsDelete()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Delete_NoHistory_Succeeds_AndLogsDelete));
        var (companyId, _) = await SeedCompanyAndCategoryAsync(ctx);
        var accessoryId = await SeedAccessoryAsync(ctx, companyId);
        var handler = new DeleteAccessoryCommandHandler(ctx, TestHelpers.CreateActionLogService(ctx, ActorId), new TestHelpers.SuperUserScope());

        var result = await handler.Handle(new DeleteAccessoryCommand { AccessoryId = accessoryId, CurrentUserId = ActorId }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Empty(await ctx.Accessories.Where(a => a.Id == accessoryId).ToListAsync());
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.Accessory && l.ActionType == ActionType.Delete);
        Assert.Equal(companyId, log.CompanyId);
    }

    // ==================== COMPANY SCOPE (controller-level list) ====================

    [Fact]
    public async Task GetAccessories_RegularUser_SeesOnlyOwnCompanyAndFloaters()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(GetAccessories_RegularUser_SeesOnlyOwnCompanyAndFloaters));
        var companyA = new Company { Name = "CT-A" };
        var companyB = new Company { Name = "CT-B" };
        ctx.Companies.AddRange(companyA, companyB);
        ctx.Accessories.AddRange(
            new Accessory { Name = "Chuột A", Qty = 5, MinAmt = 1, CompanyId = companyA.Id },
            new Accessory { Name = "Chuột B", Qty = 5, MinAmt = 1, CompanyId = companyB.Id },
            new Accessory { Name = "Chuột F", Qty = 5, MinAmt = 1, CompanyId = null });
        await ctx.SaveChangesAsync();

        var controller = new AccessoriesController(ctx, new TestHelpers.ThrowingMediator(), new TestHelpers.FakeCurrentUser(),
            new TestHelpers.FakeScope { Super = false, CompanyId = companyA.Id });

        var result = await controller.GetAccessories(null, null, null);

        var ok = Assert.IsType<OkObjectResult>(result);
        var names = TestHelpers.ReadDataStringArray(ok.Value, "name");
        Assert.Contains("Chuột A", names);
        Assert.Contains("Chuột F", names);
        Assert.DoesNotContain("Chuột B", names);
    }

    [Fact]
    public async Task GetAccessories_SuperUser_SeesAllCompanies()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(GetAccessories_SuperUser_SeesAllCompanies));
        var companyA = new Company { Name = "CT-A" };
        var companyB = new Company { Name = "CT-B" };
        ctx.Companies.AddRange(companyA, companyB);
        ctx.Accessories.AddRange(
            new Accessory { Name = "Chuột A", Qty = 5, MinAmt = 1, CompanyId = companyA.Id },
            new Accessory { Name = "Chuột B", Qty = 5, MinAmt = 1, CompanyId = companyB.Id });
        await ctx.SaveChangesAsync();

        var controller = new AccessoriesController(ctx, new TestHelpers.ThrowingMediator(), new TestHelpers.FakeCurrentUser(),
            new TestHelpers.FakeScope { Super = true });

        var result = await controller.GetAccessories(null, null, null);

        var ok = Assert.IsType<OkObjectResult>(result);
        var names = TestHelpers.ReadDataStringArray(ok.Value, "name");
        Assert.Contains("Chuột A", names);
        Assert.Contains("Chuột B", names);
    }
}

