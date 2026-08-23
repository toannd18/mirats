using System.Security.Claims;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Authorization;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using aspire_react.Server.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// Task K â€” company-scoping cho endpoint Äá»ŒC cÃ²n thiáº¿u: UsersController.GetUsers/GetUser,
/// AssetsController.GetHistory, ComponentsController.RemoveAssignment, ConsumablesController.Confirm,
/// DepartmentsController.GetAll/Get. Má»—i endpoint verify 2 chiá»u: user thÆ°á»ng bá»‹ cháº·n khÃ¡c cÃ´ng ty,
/// hoáº¡t Ä‘á»™ng Ä‘Ãºng cÃ¹ng cÃ´ng ty, Superuser khÃ´ng bá»‹ áº£nh hÆ°á»Ÿng.
/// </summary>
public class TaskKCompanyScopeReadTests
{
    private static AppDbContext CreateContext(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        // SuperUserScope so the AppDbContext global query filter is a no-op (as in the real app);
        // company-scoping is exercised explicitly via the controller's FakeScope.
        return new AppDbContext(options, new TestHelpers.SuperUserScope());
    }

    private static ClaimsPrincipal Principal(Guid localUserId)
        => new(new ClaimsIdentity(new[] { new Claim("local_user_id", localUserId.ToString()) }, "Test"));

    private static void AttachUser(ControllerBase controller, Guid localUserId)
        => controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Principal(localUserId) } };

    private static async Task<(Guid ctA, Guid ctB)> SeedCompaniesAsync(AppDbContext db)
    {
        var a = new Company { Name = "CT-A" };
        var b = new Company { Name = "CT-B" };
        db.Companies.AddRange(a, b);
        await db.SaveChangesAsync();
        return (a.Id, b.Id);
    }

    private static async Task<User> SeedUserAsync(AppDbContext db, string username, Guid? companyId)
    {
        var u = new User { Username = username, Email = $"{username}@local", FirstName = "F", LastName = "L", CompanyId = companyId };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u;
    }

    private static UsersController BuildUsersController(AppDbContext db, Guid actorId, TestHelpers.FakeScope scope)
    {
        var controller = new UsersController(
            mediator: new TestHelpers.ThrowingMediator(),
            context: db,
            actionLogService: TestHelpers.CreateActionLogService(db, actorId),
            lockoutGuard: new PermissionLockoutGuard(db),
            companyScope: scope);
        AttachUser(controller, actorId);
        return controller;
    }

    private static DepartmentsController BuildDepartmentsController(AppDbContext db, Guid actorId, TestHelpers.FakeScope scope)
    {
        var controller = new DepartmentsController(db, scope, TestHelpers.CreateActionLogService(db));
        AttachUser(controller, actorId);
        return controller;
    }

    private static AdminController BuildAdminController(AppDbContext db, Guid actorId, TestHelpers.FakeScope scope)
    {
        var controller = new AdminController(db, scope, new TestHelpers.NullCacheInvalidator(), TestHelpers.CreateActionLogService(db));
        AttachUser(controller, actorId);
        return controller;
    }

    private static AssetsController BuildAssetsController(AppDbContext db, Guid actorId, TestHelpers.FakeScope scope)
    {
        var controller = new AssetsController(db, new TestHelpers.ThrowingMediator(), new TestHelpers.FakeCurrentUser(), scope);
        AttachUser(controller, actorId);
        return controller;
    }

    private static ComponentsController BuildComponentsController(AppDbContext db, Guid actorId, TestHelpers.FakeScope scope)
    {
        var controller = new ComponentsController(db,
            new ComponentAllocationService(db, new TestHelpers.SuperUserScope(), TestHelpers.CreateActionLogService(db)), scope, TestHelpers.CreateActionLogService(db));
        AttachUser(controller, actorId);
        return controller;
    }

    private static ConsumablesController BuildConsumablesController(AppDbContext db, Guid actorId, TestHelpers.FakeScope scope)
    {
        var actionLogService = TestHelpers.CreateActionLogService(db, actorId);
        var controller = new ConsumablesController(db, actionLogService,
            new ConsumableAllocationService(db, actionLogService, new TestHelpers.SuperUserScope()), scope);
        AttachUser(controller, actorId);
        return controller;
    }

    // =========================================================================
    // UsersController.GetUsers
    // =========================================================================

    [Fact]
    public async Task GetUsers_RegularUser_OnlySeesOwnCompany_AndFloater()
    {
        await using var db = CreateContext(nameof(GetUsers_RegularUser_OnlySeesOwnCompany_AndFloater));
        var (ctA, ctB) = await SeedCompaniesAsync(db);
        var actor = await SeedUserAsync(db, "actor", ctA);
        await SeedUserAsync(db, "uA", ctA);
        await SeedUserAsync(db, "uB", ctB);
        await SeedUserAsync(db, "floater", null);

        var controller = BuildUsersController(db, actor.Id, new TestHelpers.FakeScope { Super = false, CompanyId = ctA });
        var result = await controller.GetUsers(null, null);
        var ok = Assert.IsType<OkObjectResult>(result);
        var data = TestHelpers.ReadDataStringArray(ok.Value, "username");
        Assert.Contains("actor", data);
        Assert.Contains("uA", data);
        Assert.Contains("floater", data);
        Assert.DoesNotContain("uB", data); // cross-company hidden
    }

    [Fact]
    public async Task GetUsers_RegularUser_CompanyQueryParam_Ignored()
    {
        await using var db = CreateContext(nameof(GetUsers_RegularUser_CompanyQueryParam_Ignored));
        var (ctA, ctB) = await SeedCompaniesAsync(db);
        var actor = await SeedUserAsync(db, "actor", ctA);
        await SeedUserAsync(db, "uA", ctA);
        await SeedUserAsync(db, "uB", ctB);

        // Regular user passes companyId=CT-B but scope is forced to CT-A â†’ uB still hidden.
        var controller = BuildUsersController(db, actor.Id, new TestHelpers.FakeScope { Super = false, CompanyId = ctA });
        var result = await controller.GetUsers(null, ctB);
        var ok = Assert.IsType<OkObjectResult>(result);
        var data = TestHelpers.ReadDataStringArray(ok.Value, "username");
        Assert.DoesNotContain("uB", data);
        Assert.Contains("uA", data);
    }

    [Fact]
    public async Task GetUsers_SuperUser_SeesAll()
    {
        await using var db = CreateContext(nameof(GetUsers_SuperUser_SeesAll));
        var (ctA, ctB) = await SeedCompaniesAsync(db);
        var actor = await SeedUserAsync(db, "actor", ctA);
        await SeedUserAsync(db, "uA", ctA);
        await SeedUserAsync(db, "uB", ctB);

        var controller = BuildUsersController(db, actor.Id, new TestHelpers.FakeScope { Super = true });
        var result = await controller.GetUsers(null, null);
        var ok = Assert.IsType<OkObjectResult>(result);
        var data = TestHelpers.ReadDataStringArray(ok.Value, "username");
        Assert.Contains("uA", data);
        Assert.Contains("uB", data);
    }

    // =========================================================================
    // UsersController.GetUser
    // =========================================================================

    [Fact]
    public async Task GetUser_CrossCompany_Returns404()
    {
        await using var db = CreateContext(nameof(GetUser_CrossCompany_Returns404));
        var (ctA, ctB) = await SeedCompaniesAsync(db);
        var actor = await SeedUserAsync(db, "actor", ctA);
        var uB = await SeedUserAsync(db, "uB", ctB);

        var controller = BuildUsersController(db, actor.Id, new TestHelpers.FakeScope { Super = false, CompanyId = ctA });
        Assert.IsType<NotFoundObjectResult>(await controller.GetUser(uB.Id));
    }

    [Fact]
    public async Task GetUser_SameCompany_Returns200()
    {
        await using var db = CreateContext(nameof(GetUser_SameCompany_Returns200));
        var (ctA, _) = await SeedCompaniesAsync(db);
        var actor = await SeedUserAsync(db, "actor", ctA);
        var uA = await SeedUserAsync(db, "uA", ctA);

        var controller = BuildUsersController(db, actor.Id, new TestHelpers.FakeScope { Super = false, CompanyId = ctA });
        Assert.IsType<OkObjectResult>(await controller.GetUser(uA.Id));
    }

    [Fact]
    public async Task GetUser_Floater_VisibleToRegularUser()
    {
        await using var db = CreateContext(nameof(GetUser_Floater_VisibleToRegularUser));
        var (ctA, _) = await SeedCompaniesAsync(db);
        var actor = await SeedUserAsync(db, "actor", ctA);
        var floater = await SeedUserAsync(db, "floater", null);

        var controller = BuildUsersController(db, actor.Id, new TestHelpers.FakeScope { Super = false, CompanyId = ctA });
        Assert.IsType<OkObjectResult>(await controller.GetUser(floater.Id));
    }

    [Fact]
    public async Task GetUser_SuperUser_SeesCrossCompany()
    {
        await using var db = CreateContext(nameof(GetUser_SuperUser_SeesCrossCompany));
        var (_, ctB) = await SeedCompaniesAsync(db);
        var actor = await SeedUserAsync(db, "actor", ctB);
        var uB = await SeedUserAsync(db, "uB", ctB);

        var controller = BuildUsersController(db, actor.Id, new TestHelpers.FakeScope { Super = true });
        Assert.IsType<OkObjectResult>(await controller.GetUser(uB.Id));
    }

    // =========================================================================
    // DepartmentsController.GetAll / Get
    // =========================================================================

    [Fact]
    public async Task Departments_GetAll_RegularUser_ForcedToOwnCompany_EvenWithoutQueryParam()
    {
        await using var db = CreateContext(nameof(Departments_GetAll_RegularUser_ForcedToOwnCompany_EvenWithoutQueryParam));
        var (ctA, ctB) = await SeedCompaniesAsync(db);
        var actor = await SeedUserAsync(db, "actor", ctA);
        var deptA = new Department { Name = "D-A", CompanyId = ctA };
        var deptB = new Department { Name = "D-B", CompanyId = ctB };
        var deptFloater = new Department { Name = "D-F", CompanyId = null };
        db.Departments.AddRange(deptA, deptB, deptFloater);
        await db.SaveChangesAsync();

        // No companyId query param at all â†’ scope still forced to CT-A.
        var controller = BuildDepartmentsController(db, actor.Id, new TestHelpers.FakeScope { Super = false, CompanyId = ctA });
        var result = await controller.GetAll(null);
        var ok = Assert.IsType<OkObjectResult>(result);
        var names = TestHelpers.ReadDataStringArray(ok.Value, "name");
        Assert.Contains("D-A", names);
        Assert.Contains("D-F", names);
        Assert.DoesNotContain("D-B", names);
    }

    [Fact]
    public async Task Departments_GetAll_RegularUser_CompanyQueryParam_Ignored()
    {
        await using var db = CreateContext(nameof(Departments_GetAll_RegularUser_CompanyQueryParam_Ignored));
        var (ctA, ctB) = await SeedCompaniesAsync(db);
        var actor = await SeedUserAsync(db, "actor", ctA);
        db.Departments.AddRange(new Department { Name = "D-A", CompanyId = ctA }, new Department { Name = "D-B", CompanyId = ctB });
        await db.SaveChangesAsync();

        var controller = BuildDepartmentsController(db, actor.Id, new TestHelpers.FakeScope { Super = false, CompanyId = ctA });
        var result = await controller.GetAll(ctB); // param asks for CT-B but scope forces CT-A
        var ok = Assert.IsType<OkObjectResult>(result);
        var names = TestHelpers.ReadDataStringArray(ok.Value, "name");
        Assert.DoesNotContain("D-B", names);
        Assert.Contains("D-A", names);
    }

    [Fact]
    public async Task Departments_GetAll_SuperUser_SeesAll()
    {
        await using var db = CreateContext(nameof(Departments_GetAll_SuperUser_SeesAll));
        var (ctA, ctB) = await SeedCompaniesAsync(db);
        var actor = await SeedUserAsync(db, "actor", ctA);
        db.Departments.AddRange(new Department { Name = "D-A", CompanyId = ctA }, new Department { Name = "D-B", CompanyId = ctB });
        await db.SaveChangesAsync();

        var controller = BuildDepartmentsController(db, actor.Id, new TestHelpers.FakeScope { Super = true });
        var result = await controller.GetAll(null);
        var ok = Assert.IsType<OkObjectResult>(result);
        var names = TestHelpers.ReadDataStringArray(ok.Value, "name");
        Assert.Contains("D-A", names);
        Assert.Contains("D-B", names);
    }

    [Fact]
    public async Task Departments_Get_CrossCompany_Returns404()
    {
        await using var db = CreateContext(nameof(Departments_Get_CrossCompany_Returns404));
        var (ctA, ctB) = await SeedCompaniesAsync(db);
        var actor = await SeedUserAsync(db, "actor", ctA);
        var deptB = new Department { Name = "D-B", CompanyId = ctB };
        db.Departments.Add(deptB);
        await db.SaveChangesAsync();

        var controller = BuildDepartmentsController(db, actor.Id, new TestHelpers.FakeScope { Super = false, CompanyId = ctA });
        Assert.IsType<NotFoundObjectResult>(await controller.Get(deptB.Id));
    }

    // =========================================================================
    // AdminController.GetLocations (Task U)
    // =========================================================================

    [Fact]
    public async Task Locations_GetAll_RegularUser_ForcedToOwnCompany_EvenWithoutQueryParam()
    {
        await using var db = CreateContext(nameof(Locations_GetAll_RegularUser_ForcedToOwnCompany_EvenWithoutQueryParam));
        var (ctA, ctB) = await SeedCompaniesAsync(db);
        var actor = await SeedUserAsync(db, "actor", ctA);
        db.Locations.AddRange(
            new Location { Name = "LOC-A", CompanyId = ctA },
            new Location { Name = "LOC-B", CompanyId = ctB },
            new Location { Name = "LOC-F", CompanyId = null });
        await db.SaveChangesAsync();

        // No companyId query param â†’ scope still forced to CT-A.
        var controller = BuildAdminController(db, actor.Id, new TestHelpers.FakeScope { Super = false, CompanyId = ctA });
        var result = await controller.GetLocations(null);
        var ok = Assert.IsType<OkObjectResult>(result);
        var names = TestHelpers.ReadDataStringArray(ok.Value, "name");
        Assert.Contains("LOC-A", names);
        Assert.Contains("LOC-F", names);
        Assert.DoesNotContain("LOC-B", names);
    }

    [Fact]
    public async Task Locations_GetAll_RegularUser_CompanyQueryParam_Ignored()
    {
        await using var db = CreateContext(nameof(Locations_GetAll_RegularUser_CompanyQueryParam_Ignored));
        var (ctA, ctB) = await SeedCompaniesAsync(db);
        var actor = await SeedUserAsync(db, "actor", ctA);
        db.Locations.AddRange(new Location { Name = "LOC-A", CompanyId = ctA }, new Location { Name = "LOC-B", CompanyId = ctB });
        await db.SaveChangesAsync();

        var controller = BuildAdminController(db, actor.Id, new TestHelpers.FakeScope { Super = false, CompanyId = ctA });
        var result = await controller.GetLocations(ctB); // asks for CT-B but scope forces CT-A
        var ok = Assert.IsType<OkObjectResult>(result);
        var names = TestHelpers.ReadDataStringArray(ok.Value, "name");
        Assert.DoesNotContain("LOC-B", names);
        Assert.Contains("LOC-A", names);
    }

    [Fact]
    public async Task Locations_GetAll_SuperUser_SeesAll()
    {
        await using var db = CreateContext(nameof(Locations_GetAll_SuperUser_SeesAll));
        var (ctA, ctB) = await SeedCompaniesAsync(db);
        var actor = await SeedUserAsync(db, "actor", ctA);
        db.Locations.AddRange(new Location { Name = "LOC-A", CompanyId = ctA }, new Location { Name = "LOC-B", CompanyId = ctB });
        await db.SaveChangesAsync();

        var controller = BuildAdminController(db, actor.Id, new TestHelpers.FakeScope { Super = true });
        var result = await controller.GetLocations(null);
        var ok = Assert.IsType<OkObjectResult>(result);
        var names = TestHelpers.ReadDataStringArray(ok.Value, "name");
        Assert.Contains("LOC-A", names);
        Assert.Contains("LOC-B", names);
    }

    [Fact]
    public async Task Departments_Get_SameCompany_Returns200()
    {
        await using var db = CreateContext(nameof(Departments_Get_SameCompany_Returns200));
        var (ctA, _) = await SeedCompaniesAsync(db);
        var actor = await SeedUserAsync(db, "actor", ctA);
        var deptA = new Department { Name = "D-A", CompanyId = ctA };
        db.Departments.Add(deptA);
        await db.SaveChangesAsync();

        var controller = BuildDepartmentsController(db, actor.Id, new TestHelpers.FakeScope { Super = false, CompanyId = ctA });
        Assert.IsType<OkObjectResult>(await controller.Get(deptA.Id));
    }

    // =========================================================================
    // AssetsController.GetHistory
    // =========================================================================

    [Fact]
    public async Task GetHistory_CrossCompany_Returns404()
    {
        await using var db = CreateContext(nameof(GetHistory_CrossCompany_Returns404));
        var (ctA, ctB) = await SeedCompaniesAsync(db);
        var actor = await SeedUserAsync(db, "actor", ctA);
        var assetB = new Asset { Name = "Ast-B", AssetTag = "B-1", CompanyId = ctB };
        db.Assets.Add(assetB);
        await db.SaveChangesAsync();

        var controller = BuildAssetsController(db, actor.Id, new TestHelpers.FakeScope { Super = false, CompanyId = ctA });
        Assert.IsType<NotFoundObjectResult>(await controller.GetHistory(assetB.Id));
    }

    [Fact]
    public async Task GetHistory_SameCompany_Returns200()
    {
        await using var db = CreateContext(nameof(GetHistory_SameCompany_Returns200));
        var (ctA, _) = await SeedCompaniesAsync(db);
        var actor = await SeedUserAsync(db, "actor", ctA);
        var assetA = new Asset { Name = "Ast-A", AssetTag = "A-1", CompanyId = ctA };
        db.Assets.Add(assetA);
        await db.SaveChangesAsync();

        var controller = BuildAssetsController(db, actor.Id, new TestHelpers.FakeScope { Super = false, CompanyId = ctA });
        Assert.IsType<OkObjectResult>(await controller.GetHistory(assetA.Id));
    }

    [Fact]
    public async Task GetHistory_SuperUser_SeesCrossCompany()
    {
        await using var db = CreateContext(nameof(GetHistory_SuperUser_SeesCrossCompany));
        var (_, ctB) = await SeedCompaniesAsync(db);
        var actor = await SeedUserAsync(db, "actor", ctB);
        var assetB = new Asset { Name = "Ast-B", AssetTag = "B-1", CompanyId = ctB };
        db.Assets.Add(assetB);
        await db.SaveChangesAsync();

        var controller = BuildAssetsController(db, actor.Id, new TestHelpers.FakeScope { Super = true });
        Assert.IsType<OkObjectResult>(await controller.GetHistory(assetB.Id));
    }

    // =========================================================================
    // ComponentsController.RemoveAssignment
    // =========================================================================

    private static async Task<(Component comp, ComponentAssignment asgn)> SeedComponentWithAssignmentAsync(
        AppDbContext db, Guid companyId, Guid assetId)
    {
        var comp = new Component { Name = "Comp", TrackingType = TrackingType.Bulk, Qty = 10, CompanyId = companyId };
        db.Components.Add(comp);
        await db.SaveChangesAsync();
        var asgn = new ComponentAssignment { ComponentId = comp.Id, AssetId = assetId, AssignedQty = 1 };
        db.ComponentAssignments.Add(asgn);
        await db.SaveChangesAsync();
        return (comp, asgn);
    }

    [Fact]
    public async Task RemoveAssignment_CrossCompany_Returns404_NotRemoved()
    {
        await using var db = CreateContext(nameof(RemoveAssignment_CrossCompany_Returns404_NotRemoved));
        var (ctA, ctB) = await SeedCompaniesAsync(db);
        var actor = await SeedUserAsync(db, "actor", ctA);
        var asset = new Asset { Name = "Ast", AssetTag = "AST-1", CompanyId = ctB };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();
        var (compB, asgnB) = await SeedComponentWithAssignmentAsync(db, ctB, asset.Id);

        var controller = BuildComponentsController(db, actor.Id, new TestHelpers.FakeScope { Super = false, CompanyId = ctA });
        var result = await controller.RemoveAssignment(compB.Id, new RemoveComponentRequest(asgnB.Id));
        Assert.IsType<NotFoundObjectResult>(result);
        Assert.NotNull(await db.ComponentAssignments.FindAsync(asgnB.Id)); // khÃ´ng bá»‹ xÃ³a
    }

    [Fact]
    public async Task RemoveAssignment_SameCompany_Returns200_Removed()
    {
        await using var db = CreateContext(nameof(RemoveAssignment_SameCompany_Returns200_Removed));
        var (ctA, _) = await SeedCompaniesAsync(db);
        var actor = await SeedUserAsync(db, "actor", ctA);
        var asset = new Asset { Name = "Ast", AssetTag = "AST-1", CompanyId = ctA };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();
        var (compA, asgnA) = await SeedComponentWithAssignmentAsync(db, ctA, asset.Id);

        var controller = BuildComponentsController(db, actor.Id, new TestHelpers.FakeScope { Super = false, CompanyId = ctA });
        var result = await controller.RemoveAssignment(compA.Id, new RemoveComponentRequest(asgnA.Id));
        Assert.IsType<OkObjectResult>(result);
        Assert.Null(await db.ComponentAssignments.FindAsync(asgnA.Id)); // Ä‘Ã£ xÃ³a
    }

    // =========================================================================
    // ConsumablesController.Confirm
    // =========================================================================

    [Fact]
    public async Task Confirm_CrossCompany_Returns404_StatusUnchanged()
    {
        await using var db = CreateContext(nameof(Confirm_CrossCompany_Returns404_StatusUnchanged));
        var (ctA, ctB) = await SeedCompaniesAsync(db);
        var actor = await SeedUserAsync(db, "actor", ctA);
        var consB = new Consumable { Name = "Cons-B", CompanyId = ctB, Status = ConsumableStatus.Pending };
        db.Consumables.Add(consB);
        await db.SaveChangesAsync();

        var controller = BuildConsumablesController(db, actor.Id, new TestHelpers.FakeScope { Super = false, CompanyId = ctA });
        Assert.IsType<NotFoundObjectResult>(await controller.Confirm(consB.Id));
        var reloaded = await db.Consumables.FindAsync(consB.Id);
        Assert.Equal(ConsumableStatus.Pending, reloaded!.Status); // khÃ´ng bá»‹ confirm
    }

    [Fact]
    public async Task Confirm_SameCompany_Returns200_Confirmed()
    {
        await using var db = CreateContext(nameof(Confirm_SameCompany_Returns200_Confirmed));
        var (ctA, _) = await SeedCompaniesAsync(db);
        var actor = await SeedUserAsync(db, "actor", ctA);
        var consA = new Consumable { Name = "Cons-A", CompanyId = ctA, Status = ConsumableStatus.Pending };
        db.Consumables.Add(consA);
        await db.SaveChangesAsync();

        var controller = BuildConsumablesController(db, actor.Id, new TestHelpers.FakeScope { Super = false, CompanyId = ctA });
        Assert.IsType<OkObjectResult>(await controller.Confirm(consA.Id));
        var reloaded = await db.Consumables.FindAsync(consA.Id);
        Assert.Equal(ConsumableStatus.Confirmed, reloaded!.Status);
    }
}
