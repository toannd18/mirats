using System.Security.Claims;
using aspire_react.Server.Application.Accessories.Commands;
using aspire_react.Server.Application.Assets.Commands;
using aspire_react.Server.Application.Departments.Commands;
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
/// Task L2 â€” Company-scoping cho endpoint CREATE (Asset, Consumable, Component, Accessory, Department):
/// user thÆ°á»ng chá»‰ Ä‘Æ°á»£c táº¡o báº£n ghi cho company cá»§a mÃ¬nh (hoáº·c floater); Superuser táº¡o cho company báº¥t ká»³.
/// Má»—i endpoint verify 2 chiá»u: cháº·n Ä‘Ãºng company mismatch (400), cho phÃ©p company khá»›p, Superuser khÃ´ng bá»‹ áº£nh hÆ°á»Ÿng.
/// </summary>
public class TaskL2CreateCompanyScopeTests
{
    private static AppDbContext CreateContext(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        // SuperUserScope so the AppDbContext global query filter is a no-op; company-scoping is
        // exercised explicitly via each controller/handler's FakeScope.
        return new AppDbContext(options, new TestHelpers.SuperUserScope());
    }

    private static async Task<(Guid ctA, Guid ctB)> SeedCompaniesAsync(AppDbContext db)
    {
        var a = new Company { Name = "CT-A" };
        var b = new Company { Name = "CT-B" };
        db.Companies.AddRange(a, b);
        await db.SaveChangesAsync();
        return (a.Id, b.Id);
    }

    private static ClaimsPrincipal Principal(Guid id)
        => new(new ClaimsIdentity(new[] { new Claim("local_user_id", id.ToString()) }, "Test"));

    private static void AttachUser(ControllerBase c, Guid id)
        => c.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Principal(id) } };

    private static ConsumablesController BuildConsumables(AppDbContext db, Guid actorId, TestHelpers.FakeScope scope)
    {
        var actionLog = TestHelpers.CreateActionLogService(db, actorId);
        var c = new ConsumablesController(TestHelpers.BuildMediator(db, scope, actorId));
        AttachUser(c, actorId);
        return c;
    }

    private static ComponentsController BuildComponents(AppDbContext db, Guid actorId, TestHelpers.FakeScope scope)
    {
        var c = new ComponentsController(TestHelpers.BuildMediator(db, scope, actorId));
        AttachUser(c, actorId);
        return c;
    }

    // [Giai đoạn 1] Departments migrated to MediatR: Create scope tests now drive the command
    // handler directly (same substance — scope rule + DB outcome; controller is a thin Send() map).
    private static CreateDepartmentCommandHandler BuildCreateDepartmentHandler(AppDbContext db, TestHelpers.FakeScope scope)
        => new(db, scope);

    // =========================================================================
    // CreateAssetCommandHandler
    // =========================================================================

    [Fact]
    public async Task CreateAsset_CrossCompany_Rejected_NotCreated()
    {
        await using var ctx = CreateContext(nameof(CreateAsset_CrossCompany_Rejected_NotCreated));
        var (ctA, ctB) = await SeedCompaniesAsync(ctx);
        var actor = new User { Username = "actor", Email = "a@l", FirstName = "F", LastName = "L", CompanyId = ctA };
        ctx.Users.Add(actor);
        await ctx.SaveChangesAsync();

        var handler = new CreateAssetCommandHandler(ctx, TestHelpers.CreateActionLogService(ctx, actor.Id),
            new TestHelpers.FakeScope { Super = false, CompanyId = ctA }, new AssetTagGenerator(ctx));
        var result = await handler.Handle(new CreateAssetCommand
        {
            AssetTag = "AST-CROSS",
            Name = "Cross",
            CompanyId = ctB,
            CurrentUserId = actor.Id
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("COMPANY_MISMATCH", result.ErrorCode);
        Assert.Empty(await ctx.Assets.IgnoreQueryFilters().Where(a => a.AssetTag == "AST-CROSS").ToListAsync());
    }

    [Fact]
    public async Task CreateAsset_SameCompany_Created()
    {
        await using var ctx = CreateContext(nameof(CreateAsset_SameCompany_Created));
        var (ctA, _) = await SeedCompaniesAsync(ctx);
        var actor = new User { Username = "actor", Email = "a@l", FirstName = "F", LastName = "L", CompanyId = ctA };
        ctx.Users.Add(actor);
        await ctx.SaveChangesAsync();

        var handler = new CreateAssetCommandHandler(ctx, TestHelpers.CreateActionLogService(ctx, actor.Id),
            new TestHelpers.FakeScope { Super = false, CompanyId = ctA }, new AssetTagGenerator(ctx));
        var result = await handler.Handle(new CreateAssetCommand
        {
            AssetTag = "AST-OK",
            Name = "OK",
            CompanyId = ctA,
            CurrentUserId = actor.Id
        }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(await ctx.Assets.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.AssetTag == "AST-OK"));
    }

    [Fact]
    public async Task CreateAsset_SuperUser_CrossCompany_Created()
    {
        await using var ctx = CreateContext(nameof(CreateAsset_SuperUser_CrossCompany_Created));
        var (_, ctB) = await SeedCompaniesAsync(ctx);
        var actor = new User { Username = "actor", Email = "a@l", FirstName = "F", LastName = "L" };
        ctx.Users.Add(actor);
        await ctx.SaveChangesAsync();

        var handler = new CreateAssetCommandHandler(ctx, TestHelpers.CreateActionLogService(ctx, actor.Id),
            new TestHelpers.FakeScope { Super = true }, new AssetTagGenerator(ctx));
        var result = await handler.Handle(new CreateAssetCommand
        {
            AssetTag = "AST-SU",
            Name = "SU",
            CompanyId = ctB,
            CurrentUserId = actor.Id
        }, CancellationToken.None);

        Assert.True(result.Success);
    }

    // =========================================================================
    // CreateAccessoryCommandHandler
    // =========================================================================

    [Fact]
    public async Task CreateAccessory_CrossCompany_Rejected_NotCreated()
    {
        await using var ctx = CreateContext(nameof(CreateAccessory_CrossCompany_Rejected_NotCreated));
        var (ctA, ctB) = await SeedCompaniesAsync(ctx);
        var actor = new User { Username = "actor", Email = "a@l", FirstName = "F", LastName = "L", CompanyId = ctA };
        ctx.Users.Add(actor);
        await ctx.SaveChangesAsync();

        var handler = new CreateAccessoryCommandHandler(ctx,
            new TestHelpers.FakeScope { Super = false, CompanyId = ctA });
        var result = await handler.Handle(new CreateAccessoryCommand
        {
            Name = "ACC-CROSS",
            Qty = 1,
            MinAmt = 0,
            CompanyId = ctB,
            CurrentUserId = actor.Id
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("COMPANY_MISMATCH", result.ErrorCode);
        Assert.Empty(await ctx.Accessories.IgnoreQueryFilters().Where(x => x.Name == "ACC-CROSS").ToListAsync());
    }

    [Fact]
    public async Task CreateAccessory_SameCompany_Created()
    {
        await using var ctx = CreateContext(nameof(CreateAccessory_SameCompany_Created));
        var (ctA, _) = await SeedCompaniesAsync(ctx);
        var actor = new User { Username = "actor", Email = "a@l", FirstName = "F", LastName = "L", CompanyId = ctA };
        ctx.Users.Add(actor);
        await ctx.SaveChangesAsync();

        var handler = new CreateAccessoryCommandHandler(ctx,
            new TestHelpers.FakeScope { Super = false, CompanyId = ctA });
        var result = await handler.Handle(new CreateAccessoryCommand
        {
            Name = "ACC-OK",
            Qty = 1,
            MinAmt = 0,
            CompanyId = ctA,
            CurrentUserId = actor.Id
        }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(await ctx.Accessories.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Name == "ACC-OK"));
    }

    [Fact]
    public async Task CreateAccessory_SuperUser_CrossCompany_Created()
    {
        await using var ctx = CreateContext(nameof(CreateAccessory_SuperUser_CrossCompany_Created));
        var (_, ctB) = await SeedCompaniesAsync(ctx);
        var actor = new User { Username = "actor", Email = "a@l", FirstName = "F", LastName = "L" };
        ctx.Users.Add(actor);
        await ctx.SaveChangesAsync();

        var handler = new CreateAccessoryCommandHandler(ctx,
            new TestHelpers.FakeScope { Super = true });
        var result = await handler.Handle(new CreateAccessoryCommand
        {
            Name = "ACC-SU",
            Qty = 1,
            MinAmt = 0,
            CompanyId = ctB,
            CurrentUserId = actor.Id
        }, CancellationToken.None);

        Assert.True(result.Success);
    }

    // =========================================================================
    // ConsumablesController.Create
    // =========================================================================

    private static CreateConsumableRequest ConsReq(Guid? companyId)
        => new("CON", null, 1, 0, null, null, null, null, companyId, null, null, null, null, null, null);

    [Fact]
    public async Task Consumable_Create_CrossCompany_Returns400_NotCreated()
    {
        await using var db = CreateContext(nameof(Consumable_Create_CrossCompany_Returns400_NotCreated));
        var (ctA, ctB) = await SeedCompaniesAsync(db);
        var actor = new User { Username = "actor", Email = "a@l", FirstName = "F", LastName = "L", CompanyId = ctA };
        db.Users.Add(actor);
        await db.SaveChangesAsync();

        var controller = BuildConsumables(db, actor.Id, new TestHelpers.FakeScope { Super = false, CompanyId = ctA });
        var result = await controller.Create(ConsReq(ctB));
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await db.Consumables.IgnoreQueryFilters().Where(c => c.Name == "CON").ToListAsync());
    }

    [Fact]
    public async Task Consumable_Create_SameCompany_Returns201_Created()
    {
        await using var db = CreateContext(nameof(Consumable_Create_SameCompany_Returns201_Created));
        var (ctA, _) = await SeedCompaniesAsync(db);
        var actor = new User { Username = "actor", Email = "a@l", FirstName = "F", LastName = "L", CompanyId = ctA };
        db.Users.Add(actor);
        await db.SaveChangesAsync();

        var controller = BuildConsumables(db, actor.Id, new TestHelpers.FakeScope { Super = false, CompanyId = ctA });
        var result = await controller.Create(ConsReq(ctA));
        Assert.IsType<CreatedAtActionResult>(result);
        Assert.NotNull(await db.Consumables.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Name == "CON"));
    }

    [Fact]
    public async Task Consumable_Create_SuperUser_CrossCompany_Returns201()
    {
        await using var db = CreateContext(nameof(Consumable_Create_SuperUser_CrossCompany_Returns201));
        var (_, ctB) = await SeedCompaniesAsync(db);
        var actor = new User { Username = "actor", Email = "a@l", FirstName = "F", LastName = "L" };
        db.Users.Add(actor);
        await db.SaveChangesAsync();

        var controller = BuildConsumables(db, actor.Id, new TestHelpers.FakeScope { Super = true });
        var result = await controller.Create(ConsReq(ctB));
        Assert.IsType<CreatedAtActionResult>(result);
    }

    // =========================================================================
    // ComponentsController.Create
    // =========================================================================

    private static async Task<(Guid ctA, Guid ctB, Guid catId)> SeedCompaniesAndComponentCategoryAsync(AppDbContext db)
    {
        var (ctA, ctB) = await SeedCompaniesAsync(db);
        var cat = new Category { Name = "CompCat", CategoryType = CategoryType.Component };
        db.Categories.Add(cat);
        await db.SaveChangesAsync();
        return (ctA, ctB, cat.Id);
    }

    private static CreateComponentRequest CompReq(Guid catId, Guid? companyId)
        => new("COMP", null, 1, 0, catId, null, companyId, null, null, null, null, null, null, null);

    [Fact]
    public async Task Component_Create_CrossCompany_Returns400_NotCreated()
    {
        await using var db = CreateContext(nameof(Component_Create_CrossCompany_Returns400_NotCreated));
        var (ctA, ctB, catId) = await SeedCompaniesAndComponentCategoryAsync(db);
        var actor = new User { Username = "actor", Email = "a@l", FirstName = "F", LastName = "L", CompanyId = ctA };
        db.Users.Add(actor);
        await db.SaveChangesAsync();

        var controller = BuildComponents(db, actor.Id, new TestHelpers.FakeScope { Super = false, CompanyId = ctA });
        var result = await controller.Create(CompReq(catId, ctB));
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await db.Components.IgnoreQueryFilters().Where(c => c.Name == "COMP").ToListAsync());
    }

    [Fact]
    public async Task Component_Create_SameCompany_Returns201_Created()
    {
        await using var db = CreateContext(nameof(Component_Create_SameCompany_Returns201_Created));
        var (ctA, _, catId) = await SeedCompaniesAndComponentCategoryAsync(db);
        var actor = new User { Username = "actor", Email = "a@l", FirstName = "F", LastName = "L", CompanyId = ctA };
        db.Users.Add(actor);
        await db.SaveChangesAsync();

        var controller = BuildComponents(db, actor.Id, new TestHelpers.FakeScope { Super = false, CompanyId = ctA });
        var result = await controller.Create(CompReq(catId, ctA));
        Assert.IsType<CreatedAtActionResult>(result);
        Assert.NotNull(await db.Components.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Name == "COMP"));
    }

    [Fact]
    public async Task Component_Create_SuperUser_CrossCompany_Returns201()
    {
        await using var db = CreateContext(nameof(Component_Create_SuperUser_CrossCompany_Returns201));
        var (_, ctB, catId) = await SeedCompaniesAndComponentCategoryAsync(db);
        var actor = new User { Username = "actor", Email = "a@l", FirstName = "F", LastName = "L" };
        db.Users.Add(actor);
        await db.SaveChangesAsync();

        var controller = BuildComponents(db, actor.Id, new TestHelpers.FakeScope { Super = true });
        var result = await controller.Create(CompReq(catId, ctB));
        Assert.IsType<CreatedAtActionResult>(result);
    }

    // =========================================================================
    // DepartmentsController.Create
    // =========================================================================

    [Fact]
    public async Task Department_Create_CrossCompany_Returns400_NotCreated()
    {
        await using var db = CreateContext(nameof(Department_Create_CrossCompany_Returns400_NotCreated));
        var (ctA, ctB) = await SeedCompaniesAsync(db);
        var actor = new User { Username = "actor", Email = "a@l", FirstName = "F", LastName = "L", CompanyId = ctA };
        db.Users.Add(actor);
        await db.SaveChangesAsync();

        var result = await BuildCreateDepartmentHandler(db, new TestHelpers.FakeScope { Super = false, CompanyId = ctA })
            .Handle(new CreateDepartmentCommand("DEPT-X", ctB, null, null, null, actor.Id), CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("COMPANY_MISMATCH", result.ErrorCode);
        Assert.Empty(await db.Departments.IgnoreQueryFilters().Where(d => d.Name == "DEPT-X").ToListAsync());
    }

    [Fact]
    public async Task Department_Create_SameCompany_Returns201_Created()
    {
        await using var db = CreateContext(nameof(Department_Create_SameCompany_Returns201_Created));
        var (ctA, _) = await SeedCompaniesAsync(db);
        var actor = new User { Username = "actor", Email = "a@l", FirstName = "F", LastName = "L", CompanyId = ctA };
        db.Users.Add(actor);
        await db.SaveChangesAsync();

        var result = await BuildCreateDepartmentHandler(db, new TestHelpers.FakeScope { Super = false, CompanyId = ctA })
            .Handle(new CreateDepartmentCommand("DEPT-OK", ctA, null, null, null, actor.Id), CancellationToken.None);
        Assert.True(result.Success);
        Assert.NotNull(await db.Departments.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Name == "DEPT-OK"));
    }

    [Fact]
    public async Task Department_Create_SuperUser_CrossCompany_Returns201()
    {
        await using var db = CreateContext(nameof(Department_Create_SuperUser_CrossCompany_Returns201));
        var (_, ctB) = await SeedCompaniesAsync(db);
        var actor = new User { Username = "actor", Email = "a@l", FirstName = "F", LastName = "L" };
        db.Users.Add(actor);
        await db.SaveChangesAsync();

        var result = await BuildCreateDepartmentHandler(db, new TestHelpers.FakeScope { Super = true })
            .Handle(new CreateDepartmentCommand("DEPT-SU", ctB, null, null, null, actor.Id), CancellationToken.None);
        Assert.True(result.Success);
    }
}
