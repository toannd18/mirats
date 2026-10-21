using System.Reflection;
using System.Security.Claims;
using aspire_react.Server.Application.Components.Queries;
using aspire_react.Server.Application.Consumables.Queries;
using aspire_react.Server.Application.Dashboard.Queries;
using aspire_react.Server.Application.Systems.Commands;
using aspire_react.Server.Application.Systems.Queries;
using aspire_react.Server.Domain.Authorization;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Authorization;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

public class SystemSparePartTests
{
    private sealed class SingleCompanyScope(Guid companyId) : aspire_react.Server.Domain.Interfaces.ICompanyScopeService
    {
        public bool IsSuperUser() => false;
        public Task<List<Guid>> GetUserCompanyIdsAsync() => Task.FromResult(new List<Guid> { companyId });
        public Task<Guid?> GetCurrentUserCompanyIdAsync() => Task.FromResult<Guid?>(companyId);
        public Task<bool> IsCompanyIdInUserScopeAsync(Guid id) => Task.FromResult(id == companyId);
    }

    private static AppDbContext InMemoryDb()
        => TestHelpers.CreateContext("spare-" + Guid.NewGuid().ToString("N"));

    private sealed class CapturingActionLogService(AppDbContext db)
        : aspire_react.Server.Domain.Interfaces.IActionLogService
    {
        public void LogAction(
            ItemType itemType, Guid itemId, ActionType actionType, Guid? loggedByUserId = null,
            AssignmentTargetType? targetType = null, Guid? targetId = null, string? note = null,
            string? logMeta = null, Guid? locationId = null, Guid? companyId = null,
            string? fileName = null)
        { }

        public void Log(ActionLogEntry entry) => db.ActionLogs.Add(entry.Build());

        public Task<Guid> GetCurrentUserIdAsync() => Task.FromResult(Guid.Empty);
    }

    [Fact]
    public async Task Movement_IssueReturn_UpdatesEveryRemainingEndpointConsistently()
    {
        await using var db = InMemoryDb();
        var scope = new TestHelpers.SuperUserScope();
        var company = new Company { Name = "Movement Co" };
        var system = new SystemInfo { Code = "SPR-MOV-001", Name = "Movement system", CompanyId = company.Id };
        var component = new Component
        {
            Name = "Movement RAM",
            CompanyId = company.Id,
            Qty = 10,
            MinAmt = 7,
            TrackingType = TrackingType.Bulk,
        };
        var consumable = new Consumable
        {
            Name = "Movement toner",
            CompanyId = company.Id,
            Qty = 10,
            MinAmt = 0,
            Status = ConsumableStatus.Confirmed,
        };
        db.AddRange(company, system, component, consumable);
        await db.SaveChangesAsync();

        var handler = new RecordSystemSparePartMovementCommandHandler(
            db, scope, new CapturingActionLogService(db));
        var actor = Guid.NewGuid();

        var issued = await handler.Handle(new RecordSystemSparePartMovementCommand(
            system.Id, component.Id, null, 4, SparePartMovementDirection.Issue, "issue", actor), default);
        Assert.True(issued.Success);
        Assert.Equal(6, issued.Remaining);
        Assert.Equal(4, issued.IssuedToSystem);

        var spareRows = await new GetSystemSparePartsQueryHandler(db, scope)
            .Handle(new GetSystemSparePartsQuery(system.Id), default);
        var spareRow = Assert.Single(spareRows!);
        Assert.Equal(component.Id, spareRow.ItemId);
        Assert.Equal("Component", spareRow.Type);
        Assert.Equal(6, spareRow.Remaining);
        Assert.Equal(4, spareRow.IssuedToSystem);
        Assert.NotNull(spareRow.LastIssuedAt);

        var detail = await new GetComponentByIdQueryHandler(db, scope)
            .Handle(new GetComponentByIdQuery(component.Id), default);
        Assert.Equal(6, detail!.Remaining);

        var list = await new ListComponentsQueryHandler(db, scope)
            .Handle(new ListComponentsQuery(null, null, null, null), default);
        Assert.Equal(6, list.Items.Single(i => i.Id == component.Id).Remaining);

        var lowStock = await new GetLowStockQueryHandler(db, scope)
            .Handle(new GetLowStockQuery(null), default);
        Assert.Equal(6, lowStock.Single(r => r.Id == component.Id).Remaining);
        var summary = await new GetDashboardSummaryQueryHandler(db, scope)
            .Handle(new GetDashboardSummaryQuery(), default);
        Assert.Equal(1, summary.LowStockCount);

        var consumableIssued = await handler.Handle(new RecordSystemSparePartMovementCommand(
            system.Id, null, consumable.Id, 3, SparePartMovementDirection.Issue, null, actor), default);
        Assert.True(consumableIssued.Success);
        Assert.Equal(7, consumableIssued.Remaining);
        var consumableDetail = await new GetConsumableByIdQueryHandler(db, scope)
            .Handle(new GetConsumableByIdQuery(consumable.Id), default);
        Assert.Equal(7, consumableDetail!.Remaining);
        var consumableList = await new ListConsumablesQueryHandler(db, scope)
            .Handle(new ListConsumablesQuery(null, null, null), default);
        Assert.Equal(7, consumableList.Items.Single(i => i.Id == consumable.Id).Remaining);

        var returned = await handler.Handle(new RecordSystemSparePartMovementCommand(
            system.Id, component.Id, null, 2, SparePartMovementDirection.Return, "return", actor), default);
        Assert.True(returned.Success);
        Assert.Equal(8, returned.Remaining);
        Assert.Equal(2, returned.IssuedToSystem);

        var afterReturn = await new GetComponentByIdQueryHandler(db, scope)
            .Handle(new GetComponentByIdQuery(component.Id), default);
        Assert.Equal(8, afterReturn!.Remaining);
        var spareAfterReturn = await new GetSystemSparePartsQueryHandler(db, scope)
            .Handle(new GetSystemSparePartsQuery(system.Id), default);
        var componentAfterReturn = spareAfterReturn!.Single(r => r.ItemId == component.Id);
        Assert.Equal(8, componentAfterReturn.Remaining);
        Assert.Equal(2, componentAfterReturn.IssuedToSystem);

        var summaryAfterReturn = await new GetDashboardSummaryQueryHandler(db, scope)
            .Handle(new GetDashboardSummaryQuery(), default);
        Assert.Equal(0, summaryAfterReturn.LowStockCount);
    }

    [Fact]
    public async Task Movement_RejectsOverIssue_OverReturn_Serial_AndCrossCompany()
    {
        await using var db = InMemoryDb();
        var scope = new TestHelpers.SuperUserScope();
        var company = new Company { Name = "Guard movement Co" };
        var system = new SystemInfo { Code = "SPR-MOV-002", Name = "Guard movement", CompanyId = company.Id };
        var component = new Component
        {
            Name = "Guard movement comp",
            CompanyId = company.Id,
            Qty = 5,
            TrackingType = TrackingType.Bulk,
        };
        var serial = new Component
        {
            Name = "Serial comp",
            CompanyId = company.Id,
            Qty = 1,
            TrackingType = TrackingType.Serial,
        };
        db.AddRange(company, system, component, serial);
        await db.SaveChangesAsync();

        var handler = new RecordSystemSparePartMovementCommandHandler(
            db, scope, new CapturingActionLogService(db));
        var actor = Guid.NewGuid();

        var overIssue = await handler.Handle(new RecordSystemSparePartMovementCommand(
            system.Id, component.Id, null, 6, SparePartMovementDirection.Issue, null, actor), default);
        Assert.False(overIssue.Success);
        Assert.Equal("SPARE_PART_INSUFFICIENT_STOCK", overIssue.ErrorCode);

        var overReturn = await handler.Handle(new RecordSystemSparePartMovementCommand(
            system.Id, component.Id, null, 1, SparePartMovementDirection.Return, null, actor), default);
        Assert.False(overReturn.Success);
        Assert.Equal("SPARE_PART_RETURN_EXCEEDS_ISSUED", overReturn.ErrorCode);

        var serialIssue = await handler.Handle(new RecordSystemSparePartMovementCommand(
            system.Id, serial.Id, null, 1, SparePartMovementDirection.Issue, null, actor), default);
        Assert.False(serialIssue.Success);
        Assert.Equal("SPARE_PART_SERIAL_UNSUPPORTED", serialIssue.ErrorCode);

        var invalidItem = await handler.Handle(new RecordSystemSparePartMovementCommand(
            system.Id, component.Id, component.Id, 1, SparePartMovementDirection.Issue, null, actor), default);
        Assert.Equal("INVALID_SPARE_ITEM", invalidItem.ErrorCode);

        var otherCompanySystem = new SystemInfo
        {
            Code = "SPR-MOV-OTHER",
            Name = "Other company",
            CompanyId = Guid.NewGuid(),
        };
        db.SystemInfos.Add(otherCompanySystem);
        await db.SaveChangesAsync();
        var crossCompany = await new RecordSystemSparePartMovementCommandHandler(
                db, new SingleCompanyScope(company.Id), new CapturingActionLogService(db))
            .Handle(new RecordSystemSparePartMovementCommand(
                otherCompanySystem.Id, component.Id, null, 1, SparePartMovementDirection.Issue, null, actor), default);
        Assert.False(crossCompany.Success);
        Assert.Equal("NOT_FOUND", crossCompany.ErrorCode);

        Assert.Empty(db.SystemSparePartMovements);
    }

    [Fact]
    public void MovementEndpoints_AreAuthenticated_AndPermissionHandlerEnforcesCheckoutPolicies()
    {
        var controllerType = typeof(SystemsController);
        Assert.NotNull(controllerType.GetCustomAttribute<AuthorizeAttribute>(inherit: true));
        Assert.NotNull(controllerType.GetMethod(nameof(SystemsController.IssueSparePart),
            BindingFlags.Instance | BindingFlags.Public));
        Assert.NotNull(controllerType.GetMethod(nameof(SystemsController.ReturnSparePart),
            BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public async Task MovementPermission_WithoutGrant_IsDenied_WithGrant_IsAllowed()
    {
        await using var db = InMemoryDb();
        var user = new User { Username = "qa-spare", Email = "qa-spare@local" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("local_user_id", user.Id.ToString()) }, "Test"));
        Assert.False(await EvaluatePermissionAsync(db, principal, "components.checkout"));
        Assert.False(await EvaluatePermissionAsync(db, principal, "consumables.checkout"));

        db.UserPermissions.Add(new UserPermission
        {
            UserId = user.Id,
            PermissionKey = "components.checkout",
            Value = PermissionValue.Grant,
        });
        await db.SaveChangesAsync();

        Assert.True(await EvaluatePermissionAsync(db, principal, "components.checkout"));
        Assert.False(await EvaluatePermissionAsync(db, principal, "consumables.checkout"));
    }

    private static async Task<bool> EvaluatePermissionAsync(
        AppDbContext db, ClaimsPrincipal principal, string permissionKey)
    {
        var requirement = new PermissionRequirement(permissionKey);
        var authContext = new AuthorizationHandlerContext(
            new IAuthorizationRequirement[] { requirement }, principal, resource: null);
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = principal },
        };
        var handler = new PermissionHandler(db, httpContextAccessor);
        await handler.HandleAsync(authContext);
        return authContext.HasSucceeded;
    }

    [ThrowawayDbFact]
    public async Task ConcurrentIssues_DoNotOverIssue_OnRealPostgres()
    {
        var connectionString = Environment.GetEnvironmentVariable("MIRATS_TEST_POSTGRES")?.Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Thiếu MIRATS_TEST_POSTGRES.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("aspire-react.Server"))
            .Options;
        await using (var setup = new AppDbContext(options, new TestHelpers.SuperUserScope()))
        {
            await ThrowawayDbMigrator.MigrateOnceAsync(setup, connectionString);
        }

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var company = new Company { Name = "SPARE-RACE-" + suffix, Code = "SPR-" + suffix };
        var system = new SystemInfo { Code = "SPR-RACE-" + suffix, Name = "Race system", CompanyId = company.Id };
        var component = new Component
        {
            Name = "Race component " + suffix,
            CompanyId = company.Id,
            Qty = 10,
            TrackingType = TrackingType.Bulk,
        };
        var actor = new User
        {
            Username = "qa-spare-race-" + suffix,
            Email = $"qa-spare-race-{suffix}@local",
            FirstName = "QA",
            LastName = "Spare",
        };
        await using (var setup = new AppDbContext(options, new TestHelpers.SuperUserScope()))
        {
            setup.AddRange(company, system, component, actor);
            await setup.SaveChangesAsync();
        }

        var scope = new TestHelpers.SuperUserScope();
        await using var db1 = new AppDbContext(options, scope);
        await using var db2 = new AppDbContext(options, scope);
        var handler1 = new RecordSystemSparePartMovementCommandHandler(
            db1, scope, new CapturingActionLogService(db1));
        var handler2 = new RecordSystemSparePartMovementCommandHandler(
            db2, scope, new CapturingActionLogService(db2));
        var command1 = new RecordSystemSparePartMovementCommand(
            system.Id, component.Id, null, 6, SparePartMovementDirection.Issue, "race 1", actor.Id);
        var command2 = new RecordSystemSparePartMovementCommand(
            system.Id, component.Id, null, 6, SparePartMovementDirection.Issue, "race 2", actor.Id);

        var results = await Task.WhenAll(
            handler1.Handle(command1, CancellationToken.None),
            handler2.Handle(command2, CancellationToken.None));

        Assert.Single(results, r => r.Success);
        Assert.Single(results, r => !r.Success && r.ErrorCode == "SPARE_PART_INSUFFICIENT_STOCK");
        Assert.DoesNotContain(results, r => r.Remaining < 0);

        await using var verify = new AppDbContext(options, scope);
        var movements = await verify.SystemSparePartMovements.AsNoTracking()
            .Where(m => m.SystemInfoId == system.Id && m.ComponentId == component.Id)
            .ToListAsync();
        Assert.Single(movements);
        Assert.Equal(6, movements.Sum(m => m.Quantity));
        var saved = await verify.Components.AsNoTracking()
            .Include(c => c.Assignments).Include(c => c.Units).Include(c => c.SparePartMovements)
            .SingleAsync(c => c.Id == component.Id);
        Assert.Equal(4, aspire_react.Server.Application.Common.Inventory.InventoryRemaining.ForComponent(saved));
    }
}
