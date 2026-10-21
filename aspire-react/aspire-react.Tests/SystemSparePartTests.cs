using aspire_react.Server.Application.Components.Commands;
using aspire_react.Server.Application.Consumables.Commands;
using aspire_react.Server.Application.Systems.Queries;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
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
    private static AppDbContext InMemoryDb(string name = "spare")
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name + Guid.NewGuid())
            .Options;
        return new AppDbContext(options, new TestHelpers.SuperUserScope());
    }

    [Fact]
    public async Task SpareParts_Status_UsesListRemainingFormula()
    {
        await using var db = InMemoryDb();
        var company = new Company { Name = "Spare Co" };
        var system = new SystemInfo { Code = "SPR-2026-001", Name = "System", CompanyId = company.Id };
        var component = new Component { Name = "RAM", CompanyId = company.Id, Qty = 10, MinAmt = 0, TrackingType = TrackingType.Bulk };
        var consumable = new Consumable { Name = "Toner", CompanyId = company.Id, Qty = 10, MinAmt = 0, Status = ConsumableStatus.Confirmed };
        db.AddRange(company, system, component, consumable);
        await db.SaveChangesAsync();
        db.ComponentAssignments.Add(new ComponentAssignment { ComponentId = component.Id, AssetId = Guid.NewGuid(), AssignedQty = 8 });
        db.ConsumableCheckouts.Add(new ConsumableCheckout { ConsumableId = consumable.Id, UserId = Guid.NewGuid(), Quantity = 7 });
        db.SystemSparePartRequirements.AddRange(
            new SystemSparePartRequirement { CompanyId = company.Id, SystemInfoId = system.Id, ComponentId = component.Id, MinQty = 5 },
            new SystemSparePartRequirement { CompanyId = company.Id, SystemInfoId = system.Id, ConsumableId = consumable.Id, MinQty = 2 });
        await db.SaveChangesAsync();

        var handler = new GetSystemSparePartsQueryHandler(db, new TestHelpers.SuperUserScope());
        var rows = await handler.Handle(new GetSystemSparePartsQuery(system.Id), default);

        Assert.NotNull(rows);
        Assert.Equal(2, rows!.Count);
        var ram = rows.Single(r => r.Type == "Component");
        var toner = rows.Single(r => r.Type == "Consumable");
        Assert.Equal(2, ram.Remaining);
        Assert.False(ram.IsSufficient); // 2 < 5
        Assert.Equal(3, toner.Remaining);
        Assert.True(toner.IsSufficient); // 3 >= 2
    }

    [Fact]
    public async Task SpareParts_OutOfCompanySystem_ReturnsNull()
    {
        await using var db = InMemoryDb();
        var own = Guid.NewGuid();
        var other = Guid.NewGuid();
        var system = new SystemInfo { Code = "SPR-2026-002", Name = "Other", CompanyId = other };
        db.SystemInfos.Add(system);
        await db.SaveChangesAsync();

        var handler = new GetSystemSparePartsQueryHandler(
            db, new SingleCompanyScope(own));
        var rows = await handler.Handle(new GetSystemSparePartsQuery(system.Id), default);

        Assert.Null(rows);
    }

    [Fact]
    public async Task DeleteGuards_ReturnSparePartErrorCode()
    {
        await using var db = InMemoryDb();
        var company = new Company { Name = "Guard Co" };
        var system = new SystemInfo { Code = "SPR-2026-003", Name = "Guard", CompanyId = company.Id };
        var component = new Component { Name = "Guard comp", CompanyId = company.Id };
        var consumable = new Consumable { Name = "Guard cons", CompanyId = company.Id };
        db.AddRange(company, system, component, consumable);
        await db.SaveChangesAsync();
        db.SystemSparePartRequirements.AddRange(
            new SystemSparePartRequirement { CompanyId = company.Id, SystemInfoId = system.Id, ComponentId = component.Id, MinQty = 1 },
            new SystemSparePartRequirement { CompanyId = company.Id, SystemInfoId = system.Id, ConsumableId = consumable.Id, MinQty = 1 });
        await db.SaveChangesAsync();

        var componentResult = await new DeleteComponentCommandHandler(db, new TestHelpers.SuperUserScope(), null!)
            .Handle(new DeleteComponentCommand(component.Id, Guid.NewGuid()), default);
        var consumableResult = await new DeleteConsumableCommandHandler(db, new TestHelpers.SuperUserScope(), null!)
            .Handle(new DeleteConsumableCommand(consumable.Id, Guid.NewGuid()), default);
        var systemResult = await new aspire_react.Server.Application.SystemInfos.Commands.DeleteSystemInfoCommandHandler(
                db, new TestHelpers.SuperUserScope())
            .Handle(new aspire_react.Server.Application.SystemInfos.Commands.DeleteSystemInfoCommand(system.Id, Guid.NewGuid()), default);

        Assert.False(componentResult.Success);
        Assert.Equal("SPARE_PART_REQUIREMENT_EXISTS", componentResult.ErrorCode);
        Assert.False(consumableResult.Success);
        Assert.Equal("SPARE_PART_REQUIREMENT_EXISTS", consumableResult.ErrorCode);
        Assert.False(systemResult.Success);
        Assert.Equal("SPARE_PART_REQUIREMENT_EXISTS", systemResult.ErrorCode);
    }

    [ThrowawayDbFact]
    public async Task Constraints_OneItem_And_Unique_OnRealPostgres()
    {
        var connectionString = Environment.GetEnvironmentVariable("MIRATS_TEST_POSTGRES")?.Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Thiếu MIRATS_TEST_POSTGRES.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("aspire-react.Server"))
            .Options;
        await using var db = new AppDbContext(options, new TestHelpers.SuperUserScope());
        await ThrowawayDbMigrator.MigrateOnceAsync(db, connectionString);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var company = new Company { Name = "SPARE-" + suffix, Code = "SPC-" + suffix };
        var system = new SystemInfo { Code = "SPR-" + suffix, Name = "Spare PG", CompanyId = company.Id };
        var component = new Component { Name = "Spare comp " + suffix, CompanyId = company.Id };
        db.AddRange(company, system, component);
        await db.SaveChangesAsync();

        db.SystemSparePartRequirements.Add(new SystemSparePartRequirement
        {
            CompanyId = company.Id,
            SystemInfoId = system.Id,
            MinQty = 1,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        db.SystemSparePartRequirements.Add(new SystemSparePartRequirement
        {
            CompanyId = company.Id,
            SystemInfoId = system.Id,
            ComponentId = component.Id,
            ConsumableId = Guid.NewGuid(),
            MinQty = 1,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        db.SystemSparePartRequirements.Add(new SystemSparePartRequirement
        {
            CompanyId = company.Id,
            SystemInfoId = system.Id,
            ComponentId = component.Id,
            MinQty = 1,
        });
        await db.SaveChangesAsync();
        db.SystemSparePartRequirements.Add(new SystemSparePartRequirement
        {
            CompanyId = company.Id,
            SystemInfoId = system.Id,
            ComponentId = component.Id,
            MinQty = 2,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
