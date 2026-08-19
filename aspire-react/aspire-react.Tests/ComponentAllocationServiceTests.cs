using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// Unit tests for the Component allocation/return/stock-in business rules.
/// Uses EF Core InMemory provider (service methods do not use raw SQL or real transactions â€”
/// the controller owns the ambient transaction, so the service itself is testable here).
/// </summary>
public class ComponentAllocationServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    /// <summary>Superuser scope so the FMCS global query filters short-circuit (see-all).</summary>
    private sealed class SuperUserScope : ICompanyScopeService
    {
        public bool IsSuperUser() => true;
        public Task<List<Guid>> GetUserCompanyIdsAsync() => Task.FromResult(new List<Guid>());
        public Task<Guid?> GetCurrentUserCompanyIdAsync() => Task.FromResult<Guid?>(null);
    }

    private static AppDbContext CreateContext(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(options, new SuperUserScope());
    }

    private static async Task<(Guid componentId, Guid assetId)> SeedAsync(
        AppDbContext ctx, TrackingType trackingType, int qty = 0, string[]? serials = null)
    {
        var company = new Company { Name = "CÃ´ng ty Test" };
        ctx.Companies.Add(company);

        var component = new Component { Name = "RAM 16GB", TrackingType = trackingType, Qty = qty, MinAmt = 1, CompanyId = company.Id };
        ctx.Components.Add(component);

        var asset = new Asset { AssetTag = "AST-001", Name = "Server 01", IsConfirmed = true, CompanyId = company.Id };
        ctx.Assets.Add(asset);
        await ctx.SaveChangesAsync();

        if (serials is { Length: > 0 })
        {
            foreach (var s in serials)
            {
                ctx.ComponentUnits.Add(new ComponentUnit
                {
                    ComponentId = component.Id,
                    SerialNo = s,
                    Status = ComponentUnitStatus.InStock
                });
            }
            component.Qty += serials.Length;
            await ctx.SaveChangesAsync(); // AppDbContext sets CreatedAt=UtcNow on Added â€” then we fix it below

            // SaveChanges overwrites CreatedAt on Added entities, so assign distinct increasing
            // timestamps afterwards (Modified state preserves CreatedAt) to make FIFO deterministic.
            var seeded = ctx.ComponentUnits.Where(u => u.ComponentId == component.Id).OrderBy(u => u.SerialNo).ToList();
            for (var i = 0; i < seeded.Count; i++)
            {
                seeded[i].CreatedAt = DateTime.UtcNow.AddSeconds(i + 1);
                seeded[i].UpdatedAt = seeded[i].CreatedAt;
            }
            await ctx.SaveChangesAsync();
        }

        return (component.Id, asset.Id);
    }

    // ==================== Bulk ====================

    [Fact]
    public async Task Bulk_Allocate_DecreasesRemaining_AndLogsCheckout()
    {
        await using var ctx = CreateContext(nameof(Bulk_Allocate_DecreasesRemaining_AndLogsCheckout));
        var (componentId, assetId) = await SeedAsync(ctx, TrackingType.Bulk, qty: 10);
        var service = new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx));

        var result = await service.AllocateAsync(componentId, assetId, quantity: 3, serialNo: null, note: "gáº¯n server", UserId);

        Assert.True(result.Success);
        var component = await ctx.Components.Include(c => c.Assignments).SingleAsync(c => c.Id == componentId);
        Assert.Equal(3, component.Assignments.Sum(a => a.AssignedQty));
        // ActionLog written in the same SaveChanges call.
        Assert.Equal(1, await ctx.ActionLogs.CountAsync(l => l.ItemType == ItemType.Component && l.ActionType == ActionType.Checkout));
    }

    [Fact]
    public async Task Bulk_AllocateMoreThanStock_Fails_WithoutLog()
    {
        await using var ctx = CreateContext(nameof(Bulk_AllocateMoreThanStock_Fails_WithoutLog));
        var (componentId, assetId) = await SeedAsync(ctx, TrackingType.Bulk, qty: 2);
        var service = new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx));

        var result = await service.AllocateAsync(componentId, assetId, quantity: 5, serialNo: null, note: null, UserId);

        Assert.False(result.Success);
        Assert.Equal("INSUFFICIENT_STOCK", result.ErrorCode);
        Assert.Empty(await ctx.ComponentAssignments.ToListAsync());
        Assert.Empty(await ctx.ActionLogs.ToListAsync());
    }

    [Fact]
    public async Task Bulk_Checkin_ReturnsQuantity_AndLogsCheckin()
    {
        await using var ctx = CreateContext(nameof(Bulk_Checkin_ReturnsQuantity_AndLogsCheckin));
        var (componentId, assetId) = await SeedAsync(ctx, TrackingType.Bulk, qty: 10);
        var service = new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx));
        await service.AllocateAsync(componentId, assetId, quantity: 5, serialNo: null, note: null, UserId);

        var result = await service.ReturnAsync(componentId, assetId, quantity: 2, serialNo: null, note: "tráº£ vá»", UserId);

        Assert.True(result.Success);
        var component = await ctx.Components.Include(c => c.Assignments).SingleAsync(c => c.Id == componentId);
        Assert.Equal(3, component.Assignments.Sum(a => a.AssignedQty));
        Assert.Equal(1, await ctx.ActionLogs.CountAsync(l => l.ItemType == ItemType.Component && l.ActionType == ActionType.Checkin));
    }

    [Fact]
    public async Task Bulk_CheckinMoreThanAllocated_Fails()
    {
        await using var ctx = CreateContext(nameof(Bulk_CheckinMoreThanAllocated_Fails));
        var (componentId, assetId) = await SeedAsync(ctx, TrackingType.Bulk, qty: 10);
        var service = new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx));
        await service.AllocateAsync(componentId, assetId, quantity: 2, serialNo: null, note: null, UserId);

        var result = await service.ReturnAsync(componentId, assetId, quantity: 9, serialNo: null, note: null, UserId);

        Assert.False(result.Success);
        Assert.Equal("INSUFFICIENT_ALLOCATION", result.ErrorCode);
    }

    // ==================== Serial ====================

    [Fact]
    public async Task Serial_StockIn_CreatesUnits_LogsPerUnit_AndSyncsQty()
    {
        await using var ctx = CreateContext(nameof(Serial_StockIn_CreatesUnits_LogsPerUnit_AndSyncsQty));
        var (componentId, _) = await SeedAsync(ctx, TrackingType.Serial, qty: 0);
        var service = new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx));

        var result = await service.StockInAsync(componentId, new[] { "SN-001", "SN-002", "SN-003" }, "nháº­p lÃ´ 1", UserId);

        Assert.True(result.Success);
        Assert.Equal(3, await ctx.ComponentUnits.CountAsync(u => u.ComponentId == componentId && u.Status == ComponentUnitStatus.InStock));
        Assert.Equal(3, (await ctx.Components.SingleAsync(c => c.Id == componentId)).Qty);
        Assert.Equal(3, await ctx.ActionLogs.CountAsync(l => l.ItemType == ItemType.ComponentUnit && l.ActionType == ActionType.StockIn));
    }

    [Fact]
    public async Task Serial_StockIn_DuplicateSerial_Rejected()
    {
        await using var ctx = CreateContext(nameof(Serial_StockIn_DuplicateSerial_Rejected));
        var (componentId, _) = await SeedAsync(ctx, TrackingType.Serial, serials: new[] { "SN-001" });
        var service = new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx));

        var result = await service.StockInAsync(componentId, new[] { "SN-001" }, null, UserId);

        Assert.False(result.Success);
        Assert.Equal("DUPLICATE_SERIAL", result.ErrorCode);
    }

    [Fact]
    public async Task Serial_StockIn_OnBulkComponent_Rejected()
    {
        await using var ctx = CreateContext(nameof(Serial_StockIn_OnBulkComponent_Rejected));
        var (componentId, _) = await SeedAsync(ctx, TrackingType.Bulk, qty: 5);
        var service = new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx));

        var result = await service.StockInAsync(componentId, new[] { "SN-001" }, null, UserId);

        Assert.False(result.Success);
        Assert.Equal("NOT_SERIAL", result.ErrorCode);
    }

    [Fact]
    public async Task Serial_Allocate_BySerial_MarksAllocated_AndLogs()
    {
        await using var ctx = CreateContext(nameof(Serial_Allocate_BySerial_MarksAllocated_AndLogs));
        var (componentId, assetId) = await SeedAsync(ctx, TrackingType.Serial, serials: new[] { "SN-001", "SN-002" });
        var service = new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx));

        var result = await service.AllocateAsync(componentId, assetId, quantity: 0, serialNo: "SN-002", note: null, UserId);

        Assert.True(result.Success);
        var unit = await ctx.ComponentUnits.SingleAsync(u => u.SerialNo == "SN-002");
        Assert.Equal(ComponentUnitStatus.Allocated, unit.Status);
        Assert.Equal(assetId, unit.CurrentAssetId);
        Assert.Equal(1, await ctx.ActionLogs.CountAsync(l => l.ItemType == ItemType.ComponentUnit && l.ActionType == ActionType.Checkout));
    }

    [Fact]
    public async Task Serial_Allocate_Fifo_WhenNoSerialSpecified()
    {
        await using var ctx = CreateContext(nameof(Serial_Allocate_Fifo_WhenNoSerialSpecified));
        var (componentId, assetId) = await SeedAsync(ctx, TrackingType.Serial, serials: new[] { "SN-001", "SN-002" });
        var service = new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx));

        var result = await service.AllocateAsync(componentId, assetId, quantity: 0, serialNo: null, note: null, UserId);

        Assert.True(result.Success);
        var unit = await ctx.ComponentUnits.SingleAsync(u => u.Status == ComponentUnitStatus.Allocated);
        Assert.Equal("SN-001", unit.SerialNo); // FIFO by CreatedAt
    }

    [Fact]
    public async Task Serial_Allocate_WrongSerial_Fails()
    {
        await using var ctx = CreateContext(nameof(Serial_Allocate_WrongSerial_Fails));
        var (componentId, assetId) = await SeedAsync(ctx, TrackingType.Serial, serials: new[] { "SN-001" });
        var service = new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx));

        var result = await service.AllocateAsync(componentId, assetId, quantity: 0, serialNo: "SN-999", note: null, UserId);

        Assert.False(result.Success);
        Assert.Equal("SERIAL_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task Serial_Allocate_OutOfStock_Fails()
    {
        await using var ctx = CreateContext(nameof(Serial_Allocate_OutOfStock_Fails));
        var (componentId, assetId) = await SeedAsync(ctx, TrackingType.Serial, serials: new[] { "SN-001" });
        var service = new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx));
        await service.AllocateAsync(componentId, assetId, quantity: 0, serialNo: "SN-001", note: null, UserId);

        var result = await service.AllocateAsync(componentId, assetId, quantity: 0, serialNo: null, note: null, UserId);

        Assert.False(result.Success);
        Assert.Equal("INSUFFICIENT_STOCK", result.ErrorCode);
    }

    [Fact]
    public async Task Serial_Checkin_BySerial_ReturnsToStock_AndLogs()
    {
        await using var ctx = CreateContext(nameof(Serial_Checkin_BySerial_ReturnsToStock_AndLogs));
        var (componentId, assetId) = await SeedAsync(ctx, TrackingType.Serial, serials: new[] { "SN-001" });
        var service = new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx));
        await service.AllocateAsync(componentId, assetId, quantity: 0, serialNo: "SN-001", note: null, UserId);

        var result = await service.ReturnAsync(componentId, assetId, quantity: 0, serialNo: "SN-001", note: "tráº£ vá»", UserId);

        Assert.True(result.Success);
        var unit = await ctx.ComponentUnits.SingleAsync(u => u.SerialNo == "SN-001");
        Assert.Equal(ComponentUnitStatus.InStock, unit.Status);
        Assert.Null(unit.CurrentAssetId);
        Assert.Equal(1, await ctx.ActionLogs.CountAsync(l => l.ItemType == ItemType.ComponentUnit && l.ActionType == ActionType.Checkin));
    }

    [Fact]
    public async Task Serial_Checkin_WrongSerial_Fails()
    {
        await using var ctx = CreateContext(nameof(Serial_Checkin_WrongSerial_Fails));
        var (componentId, assetId) = await SeedAsync(ctx, TrackingType.Serial, serials: new[] { "SN-001" });
        var service = new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx));
        await service.AllocateAsync(componentId, assetId, quantity: 0, serialNo: "SN-001", note: null, UserId);

        var result = await service.ReturnAsync(componentId, assetId, quantity: 0, serialNo: "SN-777", note: null, UserId);

        Assert.False(result.Success);
        Assert.Equal("SERIAL_NOT_ALLOCATED", result.ErrorCode);
    }

    [Fact]
    public async Task Serial_SetUnitStatus_Damaged_ClearsAsset_AndLogsMarkDamaged()
    {
        await using var ctx = CreateContext(nameof(Serial_SetUnitStatus_Damaged_ClearsAsset_AndLogsMarkDamaged));
        var (componentId, assetId) = await SeedAsync(ctx, TrackingType.Serial, serials: new[] { "SN-001" });
        var service = new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx));
        await service.AllocateAsync(componentId, assetId, quantity: 0, serialNo: "SN-001", note: null, UserId);
        var unitId = (await ctx.ComponentUnits.SingleAsync(u => u.SerialNo == "SN-001")).Id;

        var result = await service.SetUnitStatusAsync(unitId, ComponentUnitStatus.Damaged, "há»ng do nÆ°á»›c", UserId);

        Assert.True(result.Success);
        var unit = await ctx.ComponentUnits.SingleAsync(u => u.Id == unitId);
        Assert.Equal(ComponentUnitStatus.Damaged, unit.Status);
        Assert.Null(unit.CurrentAssetId);
        Assert.Equal(1, await ctx.ActionLogs.CountAsync(l => l.ItemType == ItemType.ComponentUnit && l.ActionType == ActionType.MarkDamaged));
    }

    [Fact]
    public async Task Serial_ReturnBySerialNo_LogsTargetIdOfRealAsset()
    {
        await using var ctx = CreateContext(nameof(Serial_ReturnBySerialNo_LogsTargetIdOfRealAsset));
        var (componentId, assetId) = await SeedAsync(ctx, TrackingType.Serial, serials: new[] { "SN-001" });
        var service = new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx));
        await service.AllocateAsync(componentId, assetId, quantity: 0, serialNo: "SN-001", note: null, UserId);

        // Return via serialNo WITHOUT assetId â€” the path that previously logged TargetId = null
        // (Task N: the log must record the REAL asset the serial was returned from, not the null request assetId).
        var result = await service.ReturnAsync(componentId, assetId: null, quantity: 0, serialNo: "SN-001", note: null, UserId);

        Assert.True(result.Success);
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.ComponentUnit && l.ActionType == ActionType.Checkin);
        Assert.NotNull(log.TargetId);
        Assert.Equal(assetId, log.TargetId.Value); // NOT null â€” the actual asset the serial was allocated to
    }

    // ==================== Company scoping rules ====================

    [Fact]
    public async Task Allocate_ComponentWithoutCompany_RejectedWithComponentCompanyRequired()
    {
        await using var ctx = CreateContext(nameof(Allocate_ComponentWithoutCompany_RejectedWithComponentCompanyRequired));
        var component = new Component { Name = "RAM 16GB", TrackingType = TrackingType.Bulk, Qty = 10, MinAmt = 1, CompanyId = null };
        ctx.Components.Add(component);
        var asset = new Asset { AssetTag = "AST-002", Name = "Server 02", IsConfirmed = true, CompanyId = Guid.NewGuid() };
        ctx.Assets.Add(asset);
        await ctx.SaveChangesAsync();

        var service = new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx));
        var result = await service.AllocateAsync(component.Id, asset.Id, quantity: 1, serialNo: null, note: null, UserId);

        Assert.False(result.Success);
        Assert.Equal("COMPONENT_COMPANY_REQUIRED", result.ErrorCode);
    }

    [Fact]
    public async Task Allocate_CrossCompany_RejectedWithCompanyMismatch()
    {
        await using var ctx = CreateContext(nameof(Allocate_CrossCompany_RejectedWithCompanyMismatch));
        var companyA = new Company { Name = "CT-A" };
        var companyB = new Company { Name = "CT-B" };
        ctx.Companies.AddRange(companyA, companyB);
        var component = new Component { Name = "RAM 16GB", TrackingType = TrackingType.Bulk, Qty = 10, MinAmt = 1, CompanyId = companyA.Id };
        ctx.Components.Add(component);
        var asset = new Asset { AssetTag = "AST-003", Name = "Server 03", IsConfirmed = true, CompanyId = companyB.Id };
        ctx.Assets.Add(asset);
        await ctx.SaveChangesAsync();

        var service = new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx));
        var result = await service.AllocateAsync(component.Id, asset.Id, quantity: 1, serialNo: null, note: null, UserId);

        Assert.False(result.Success);
        Assert.Equal("COMPANY_MISMATCH", result.ErrorCode);
    }

    [Fact]
    public async Task Allocate_SameCompany_Succeeds()
    {
        await using var ctx = CreateContext(nameof(Allocate_SameCompany_Succeeds));
        var (componentId, assetId) = await SeedAsync(ctx, TrackingType.Bulk, qty: 10);
        var service = new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx));

        var result = await service.AllocateAsync(componentId, assetId, quantity: 2, serialNo: null, note: null, UserId);

        Assert.True(result.Success);
    }

    // ==================== DeleteUnitAsync (soft-delete serial) ====================

    [Fact]
    public async Task Serial_DeleteUnit_Deletes_DecrementsQty_LogsDelete()
    {
        await using var ctx = CreateContext(nameof(Serial_DeleteUnit_Deletes_DecrementsQty_LogsDelete));
        var (componentId, _) = await SeedAsync(ctx, TrackingType.Serial, serials: new[] { "SN-001" });
        var unitId = (await ctx.ComponentUnits.SingleAsync(u => u.SerialNo == "SN-001")).Id;
        var service = new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx));

        var result = await service.DeleteUnitAsync(unitId, UserId);

        Assert.True(result.Success);
        // The global query filter (DeletedAt == null) hides the soft-deleted unit â€” use IgnoreQueryFilters to read it.
        var unit = await ctx.ComponentUnits.IgnoreQueryFilters().SingleAsync(u => u.Id == unitId);
        Assert.NotNull(unit.DeletedAt);
        Assert.Null(unit.CurrentAssetId);
        Assert.Equal(0, (await ctx.Components.SingleAsync(c => c.Id == componentId)).Qty);
        Assert.Equal(1, await ctx.ActionLogs.CountAsync(l => l.ItemType == ItemType.ComponentUnit && l.ActionType == ActionType.Delete));
    }

    [Fact]
    public async Task Serial_DeleteUnit_WithAllocationHistory_Blocked()
    {
        await using var ctx = CreateContext(nameof(Serial_DeleteUnit_WithAllocationHistory_Blocked));
        var (componentId, assetId) = await SeedAsync(ctx, TrackingType.Serial, serials: new[] { "SN-001" });
        var unitId = (await ctx.ComponentUnits.SingleAsync(u => u.SerialNo == "SN-001")).Id;
        // Simulate a past checkout so the audit trail must stay intact.
        ctx.ActionLogs.Add(new ActionLog
        {
            ItemType = ItemType.ComponentUnit,
            ItemId = unitId,
            ActionType = ActionType.Checkout,
            CreatedBy = UserId,
            Note = "Ä‘Ã£ cáº¥p phÃ¡t"
        });
        await ctx.SaveChangesAsync();
        var service = new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx));

        var result = await service.DeleteUnitAsync(unitId, UserId);

        Assert.False(result.Success);
        Assert.Equal("COMPONENT_UNIT_HAS_ALLOCATION_HISTORY", result.ErrorCode);
        Assert.Null((await ctx.ComponentUnits.SingleAsync(u => u.Id == unitId)).DeletedAt);
    }

    [Fact]
    public async Task Serial_DeleteUnit_AlreadyDeleted_Blocked()
    {
        await using var ctx = CreateContext(nameof(Serial_DeleteUnit_AlreadyDeleted_Blocked));
        var (componentId, _) = await SeedAsync(ctx, TrackingType.Serial, serials: new[] { "SN-001" });
        var unitId = (await ctx.ComponentUnits.SingleAsync(u => u.SerialNo == "SN-001")).Id;
        var service = new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx));
        await service.DeleteUnitAsync(unitId, UserId);

        // The global query filter (DeletedAt == null) hides the already-deleted unit, so a second
        // delete resolves it as not found (NOT_FOUND) â€” the same behaviour the original controller had.
        var result = await service.DeleteUnitAsync(unitId, UserId);

        Assert.False(result.Success);
        Assert.Equal("NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task Serial_DeleteUnit_CrossCompany_NotAllowed()
    {
        await using var ctx = CreateContext(nameof(Serial_DeleteUnit_CrossCompany_NotAllowed));
        var (componentId, _) = await SeedAsync(ctx, TrackingType.Serial, serials: new[] { "SN-001" });
        var unitId = (await ctx.ComponentUnits.SingleAsync(u => u.SerialNo == "SN-001")).Id;
        var otherCompany = new Company { Name = "CT-KhÃ¡c" };
        ctx.Companies.Add(otherCompany);
        await ctx.SaveChangesAsync();

        var service = new ComponentAllocationService(ctx, new TestHelpers.FakeScope { Super = false, CompanyId = otherCompany.Id }, TestHelpers.CreateActionLogService(ctx));
        var result = await service.DeleteUnitAsync(unitId, UserId);

        Assert.False(result.Success);
        Assert.Equal("NOT_FOUND", result.ErrorCode);
        Assert.Null((await ctx.ComponentUnits.SingleAsync(u => u.Id == unitId)).DeletedAt);
    }

    [Fact]
    public async Task Serial_DeleteUnit_NotFound()
    {
        await using var ctx = CreateContext(nameof(Serial_DeleteUnit_NotFound));
        var service = new ComponentAllocationService(ctx, new SuperUserScope(), TestHelpers.CreateActionLogService(ctx));

        var result = await service.DeleteUnitAsync(Guid.NewGuid(), UserId);

        Assert.False(result.Success);
        Assert.Equal("NOT_FOUND", result.ErrorCode);
    }
}

