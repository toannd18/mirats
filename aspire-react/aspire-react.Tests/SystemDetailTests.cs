using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using aspire_react.Server.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// SystemDetailPage read endpoints (SystemsController): assets + accessories aggregated across ALL
/// child SystemPositions of a SystemInfo, position-level filtering, empty states, company scoping
/// (defense-in-depth for regular users — 404 for out-of-scope systems; Superuser sees any company),
/// and the maintenance systemInfoId filter (SnapshotSystemInfoId).
/// </summary>
public class SystemDetailTests
{
    private sealed class SuperUserScope : ICompanyScopeService
    {
        public bool IsSuperUser() => true;
        public Task<List<Guid>> GetUserCompanyIdsAsync() => Task.FromResult(new List<Guid>());
        public Task<Guid?> GetCurrentUserCompanyIdAsync() => Task.FromResult<Guid?>(null);
    }

    /// <summary>Company-aware fake: GetUserCompanyIdsAsync respects CompanyId so the system-visibility
    /// gate (same as /action-logs/by-system) is exercised in the scoping tests.</summary>
    private sealed class CompanyScopeFake : ICompanyScopeService
    {
        public bool Super { get; set; }
        public Guid? CompanyId { get; set; }
        public bool IsSuperUser() => Super;
        public Task<List<Guid>> GetUserCompanyIdsAsync() =>
            Task.FromResult(Super || !CompanyId.HasValue ? new List<Guid>() : new List<Guid> { CompanyId.Value });
        public Task<Guid?> GetCurrentUserCompanyIdAsync() => Task.FromResult(Super ? (Guid?)null : CompanyId);
    }


    private sealed class FakeCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid GetLocalUserId() => UserId;
    }
    private static AppDbContext CreateContext(string name) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options, new SuperUserScope());

    private static async Task<(SystemInfo sys, SystemPosition posA, SystemPosition posB)> SeedSystemAsync(AppDbContext ctx)
    {
        var sys = new SystemInfo { Code = "SYS-001-COR", Name = "Hệ thống A" };
        var posA = new SystemPosition { Code = "POS-001-NOD", Name = "Vị trí 1", SystemInfo = sys };
        var posB = new SystemPosition { Code = "POS-002-NOD", Name = "Vị trí 2", SystemInfo = sys };
        ctx.SystemInfos.Add(sys);
        ctx.SystemPositions.Add(posA);
        ctx.SystemPositions.Add(posB);
        await ctx.SaveChangesAsync();
        return (sys, posA, posB);
    }

    private static async Task<Guid> SeedAssetAsync(AppDbContext ctx, SystemPosition pos, string tag)
    {
        var asset = new Asset
        {
            AssetTag = tag,
            Name = "Server " + tag,
            IsConfirmed = true,
            Status = AssetStatus.Deployed,
            SystemPositionId = pos.Id
        };
        ctx.Assets.Add(asset);
        await ctx.SaveChangesAsync();
        return asset.Id;
    }

    private static async Task<(Accessory acc, AccessoryCheckout ch)> SeedAccessoryCheckoutAsync(AppDbContext ctx, SystemPosition pos)
    {
        var acc = new Accessory { Name = "Chuột không dây", ItemNo = "MOUSE-01", Qty = 10 };
        ctx.Accessories.Add(acc);
        await ctx.SaveChangesAsync();
        var ch = new AccessoryCheckout
        {
            AccessoryId = acc.Id,
            CheckoutType = AccessoryCheckoutType.SystemPosition,
            TargetId = pos.Id,
            AssignedQty = 2,
            ReturnedQty = 0,
            CreatedByUserId = Guid.NewGuid(),
            Note = "Cấp phát test",
            CheckedOutAt = DateTime.UtcNow
        };
        ctx.AccessoryCheckouts.Add(ch);
        await ctx.SaveChangesAsync();
        return (acc, ch);
    }

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static JsonElement OkData(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return JsonSerializer.SerializeToElement(ok.Value, WebJson).GetProperty("data");
    }

    private static JsonElement OkPagination(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return JsonSerializer.SerializeToElement(ok.Value, WebJson).GetProperty("pagination");
    }


    // ==================== SYSTEM INFO (GET by id - regression for the JSON cycle bug) ====================

    [Fact]
    public async Task GetSystemInfo_ReturnsProjection_NotRawEntity() 
    {
        await using var ctx = CreateContext(nameof(GetSystemInfo_ReturnsProjection_NotRawEntity));
        var (sys, _, _) = await SeedSystemAsync(ctx);

        var controller = new SystemInfoController(ctx, new CompanyScopeFake { Super = true }, TestHelpers.CreateActionLogService(ctx));
        var result = await controller.Get(sys.Id);

        var data = OkData(result);
        Assert.Equal(sys.Id, data.GetProperty("id").GetGuid());
        Assert.Equal("SYS-001-COR", data.GetProperty("code").GetString());
        Assert.Equal(2, data.GetProperty("positions").GetArrayLength());
        Assert.False(data.TryGetProperty("positions", out var _) && data.GetProperty("positions")[0].TryGetProperty("systemInfo", out var _),
            "The projection must not embed the cyclic SystemInfo navigation back-reference.");
    }
    // ==================== ASSETS ====================

    [Fact]
    public async Task GetAssets_AggregatesAcrossAllChildPositions_OfSystemInfo()
    {
        await using var ctx = CreateContext(nameof(GetAssets_AggregatesAcrossAllChildPositions_OfSystemInfo));
        var (sys, posA, posB) = await SeedSystemAsync(ctx);
        var assetA = await SeedAssetAsync(ctx, posA, "AST-001");
        var assetB = await SeedAssetAsync(ctx, posB, "AST-002");

        var controller = new SystemsController(ctx, new CompanyScopeFake { Super = true });
        var result = await controller.GetAssets(sys.Id);

        var data = OkData(result);
        var arr = data.EnumerateArray().ToList();
        Assert.Equal(2, arr.Count);
        var ids = arr.Select(x => x.GetProperty("id").GetGuid()).OrderBy(x => x).ToList();
        Assert.Equal(new[] { assetA, assetB }.OrderBy(x => x), ids);
        // Each row carries its own SystemPosition (child-level placement).
        Assert.Contains(arr, x => x.GetProperty("systemPosition").GetProperty("id").GetGuid() == posA.Id);
        Assert.Contains(arr, x => x.GetProperty("systemPosition").GetProperty("id").GetGuid() == posB.Id);
        Assert.Equal(2, OkPagination(result).GetProperty("totalItems").GetInt32());
    }

    [Fact]
    public async Task GetAssets_FiltersBySingleSystemPosition()
    {
        await using var ctx = CreateContext(nameof(GetAssets_FiltersBySingleSystemPosition));
        var (sys, posA, posB) = await SeedSystemAsync(ctx);
        await SeedAssetAsync(ctx, posA, "AST-001");
        await SeedAssetAsync(ctx, posB, "AST-002");

        var controller = new SystemsController(ctx, new CompanyScopeFake { Super = true });
        var result = await controller.GetAssets(sys.Id, systemPositionId: posA.Id);

        var arr = OkData(result).EnumerateArray().ToList();
        Assert.Single(arr);
        Assert.Equal(posA.Id, arr[0].GetProperty("systemPosition").GetProperty("id").GetGuid());
        Assert.Equal(1, OkPagination(result).GetProperty("totalItems").GetInt32());
    }

    [Fact]
    public async Task GetAssets_EmptyWhenNoAssets()
    {
        await using var ctx = CreateContext(nameof(GetAssets_EmptyWhenNoAssets));
        var (sys, _, _) = await SeedSystemAsync(ctx);

        var controller = new SystemsController(ctx, new CompanyScopeFake { Super = true });
        var result = await controller.GetAssets(sys.Id);

        Assert.Empty(OkData(result).EnumerateArray());
        Assert.Equal(0, OkPagination(result).GetProperty("totalItems").GetInt32());
    }

    [Fact]
    public async Task GetAssets_CompanyScoped_OtherCompanySystem_ReturnsNotFound()
    {
        await using var ctx = CreateContext(nameof(GetAssets_CompanyScoped_OtherCompanySystem_ReturnsNotFound));
        var sys = new SystemInfo { Code = "SYS-001-COR", Name = "Hệ thống A", CompanyId = Guid.NewGuid() };
        var pos = new SystemPosition { Code = "POS-001-NOD", Name = "Vị trí 1", SystemInfo = sys };
        ctx.SystemInfos.Add(sys);
        ctx.SystemPositions.Add(pos);
        await ctx.SaveChangesAsync();
        await SeedAssetAsync(ctx, pos, "AST-001");

        // Regular user of a DIFFERENT company → the system itself is out of scope → 404.
        // DELIBERATE convention (NOT a bug): system-level resources return 404 (hide existence of a
        // system whose code/name is company-sensitive), unlike single maintenance records which
        // return 403. Same convention as SystemInfoController.Get + ActionLogsController.GetBySystem.
        var controller = new SystemsController(ctx, new CompanyScopeFake { CompanyId = Guid.NewGuid() });
        var result = await controller.GetAssets(sys.Id);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetAssets_Superuser_SeesSystemOfAnyCompany()
    {
        await using var ctx = CreateContext(nameof(GetAssets_Superuser_SeesSystemOfAnyCompany));
        var companyId = Guid.NewGuid();
        var sys = new SystemInfo { Code = "SYS-001-COR", Name = "Hệ thống A", CompanyId = companyId };
        var pos = new SystemPosition { Code = "POS-001-NOD", Name = "Vị trí 1", SystemInfo = sys };
        ctx.SystemInfos.Add(sys);
        ctx.SystemPositions.Add(pos);
        await ctx.SaveChangesAsync();
        await SeedAssetAsync(ctx, pos, "AST-001");

        // Superuser bypasses the company gate entirely — sees the system (and its assets) of ANY company.
        var controller = new SystemsController(ctx, new CompanyScopeFake { Super = true });
        var result = await controller.GetAssets(sys.Id);
        Assert.Single(OkData(result).EnumerateArray());
    }

    [Fact]
    public async Task GetAssets_CompanyScoped_SameCompany_ReturnsAssets()
    {
        await using var ctx = CreateContext(nameof(GetAssets_CompanyScoped_SameCompany_ReturnsAssets));
        var companyId = Guid.NewGuid();
        var sys = new SystemInfo { Code = "SYS-001-COR", Name = "Hệ thống A", CompanyId = companyId };
        var pos = new SystemPosition { Code = "POS-001-NOD", Name = "Vị trí 1", SystemInfo = sys };
        ctx.SystemInfos.Add(sys);
        ctx.SystemPositions.Add(pos);
        await ctx.SaveChangesAsync();
        await SeedAssetAsync(ctx, pos, "AST-001");

        var controller = new SystemsController(ctx, new CompanyScopeFake { CompanyId = companyId });
        var result = await controller.GetAssets(sys.Id);
        Assert.Single(OkData(result).EnumerateArray());
    }

    // ==================== ACCESSORIES ====================

    [Fact]
    public async Task GetAccessories_AggregatesPositionLevelCheckouts_UnderSystemInfo()
    {
        await using var ctx = CreateContext(nameof(GetAccessories_AggregatesPositionLevelCheckouts_UnderSystemInfo));
        var (sys, posA, posB) = await SeedSystemAsync(ctx);
        var (acc1, _) = await SeedAccessoryCheckoutAsync(ctx, posA);
        var (_, _) = await SeedAccessoryCheckoutAsync(ctx, posB);

        // A checkout to a position of ANOTHER system must NOT appear.
        var otherSys = new SystemInfo { Code = "SYS-002-COR", Name = "Hệ thống B" };
        var otherPos = new SystemPosition { Code = "POS-003-NOD", Name = "Vị trí khác", SystemInfo = otherSys };
        ctx.SystemInfos.Add(otherSys);
        ctx.SystemPositions.Add(otherPos);
        await ctx.SaveChangesAsync();
        await SeedAccessoryCheckoutAsync(ctx, otherPos);

        var controller = new SystemsController(ctx, new CompanyScopeFake { Super = true });
        var result = await controller.GetAccessories(sys.Id);

        var arr = OkData(result).EnumerateArray().ToList();
        Assert.Equal(2, arr.Count);
        Assert.All(arr, x => Assert.Equal("Chuột không dây", x.GetProperty("accessoryName").GetString()));
        // RemainingCheckedOut = AssignedQty - ReturnedQty (2 - 0).
        Assert.All(arr, x => Assert.Equal(2, x.GetProperty("remainingCheckedOut").GetInt32()));
        Assert.Contains(arr, x => x.GetProperty("systemPosition").GetProperty("id").GetGuid() == posA.Id);
        Assert.Contains(arr, x => x.GetProperty("systemPosition").GetProperty("id").GetGuid() == posB.Id);
        // CreatedByName resolves (CreatedByUser is empty in seed → falls back to null, not a crash).
        Assert.Contains(arr, x => x.GetProperty("createdByName").ValueKind == JsonValueKind.Null
            || x.GetProperty("createdByName").GetString() != null);
    }

    [Fact]
    public async Task GetAccessories_FiltersBySingleSystemPosition()
    {
        await using var ctx = CreateContext(nameof(GetAccessories_FiltersBySingleSystemPosition));
        var (sys, posA, posB) = await SeedSystemAsync(ctx);
        await SeedAccessoryCheckoutAsync(ctx, posA);
        await SeedAccessoryCheckoutAsync(ctx, posB);

        var controller = new SystemsController(ctx, new CompanyScopeFake { Super = true });
        var result = await controller.GetAccessories(sys.Id, systemPositionId: posB.Id);

        var arr = OkData(result).EnumerateArray().ToList();
        Assert.Single(arr);
        Assert.Equal(posB.Id, arr[0].GetProperty("systemPosition").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task GetAccessories_EmptyWhenNoCheckouts()
    {
        await using var ctx = CreateContext(nameof(GetAccessories_EmptyWhenNoCheckouts));
        var (sys, _, _) = await SeedSystemAsync(ctx);

        var controller = new SystemsController(ctx, new CompanyScopeFake { Super = true });
        var result = await controller.GetAccessories(sys.Id);
        Assert.Empty(OkData(result).EnumerateArray());
    }

    [Fact]
    public async Task GetAccessories_CompanyScoped_OtherCompanySystem_ReturnsNotFound()
    {
        await using var ctx = CreateContext(nameof(GetAccessories_CompanyScoped_OtherCompanySystem_ReturnsNotFound));
        var sys = new SystemInfo { Code = "SYS-001-COR", Name = "Hệ thống A", CompanyId = Guid.NewGuid() };
        var pos = new SystemPosition { Code = "POS-001-NOD", Name = "Vị trí 1", SystemInfo = sys };
        ctx.SystemInfos.Add(sys);
        ctx.SystemPositions.Add(pos);
        await ctx.SaveChangesAsync();
        await SeedAccessoryCheckoutAsync(ctx, pos);

        // Deliberate 404 convention for system-level resources (see GetAssets_CompanyScoped_..._ReturnsNotFound).
        var controller = new SystemsController(ctx, new CompanyScopeFake { CompanyId = Guid.NewGuid() });
        var result = await controller.GetAccessories(sys.Id);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetAccessories_Superuser_SeesSystemOfAnyCompany()
    {
        await using var ctx = CreateContext(nameof(GetAccessories_Superuser_SeesSystemOfAnyCompany));
        var companyId = Guid.NewGuid();
        var sys = new SystemInfo { Code = "SYS-001-COR", Name = "Hệ thống A", CompanyId = companyId };
        var pos = new SystemPosition { Code = "POS-001-NOD", Name = "Vị trí 1", SystemInfo = sys };
        ctx.SystemInfos.Add(sys);
        ctx.SystemPositions.Add(pos);
        await ctx.SaveChangesAsync();
        await SeedAccessoryCheckoutAsync(ctx, pos);

        // Same bypass as assets: the system belongs to another company but the Superuser sees it.
        var controller = new SystemsController(ctx, new CompanyScopeFake { Super = true });
        var result = await controller.GetAccessories(sys.Id);
        Assert.Single(OkData(result).EnumerateArray());
    }

    // ==================== MAINTENANCE (systemInfoId filter) ====================

    [Fact]
    public async Task GetAllMaintenances_WithSystemInfoId_FiltersBySnapshotSystemInfoId()
    {
        await using var ctx = CreateContext(nameof(GetAllMaintenances_WithSystemInfoId_FiltersBySnapshotSystemInfoId));
        var (sysA, posA, _) = await SeedSystemAsync(ctx);
        var assetA = await SeedAssetAsync(ctx, posA, "AST-001");

        var sysB = new SystemInfo { Code = "SYS-002-COR", Name = "Hệ thống B" };
        var posB = new SystemPosition { Code = "POS-003-NOD", Name = "Vị trí khác", SystemInfo = sysB };
        ctx.SystemInfos.Add(sysB);
        ctx.SystemPositions.Add(posB);
        await ctx.SaveChangesAsync();
        var assetB = await SeedAssetAsync(ctx, posB, "AST-002");

        ctx.AssetMaintenances.Add(new AssetMaintenance
        {
            AssetId = assetA, Title = "Bảo trì hệ thống A",
            Type = AssetMaintenanceType.Maintenance, StartDate = DateTime.UtcNow,
            CompanyId = Guid.Empty,
            SnapshotSystemInfoId = sysA.Id, SnapshotSystemInfoName = sysA.Name
        });
        ctx.AssetMaintenances.Add(new AssetMaintenance
        {
            AssetId = assetB, Title = "Bảo trì hệ thống B",
            Type = AssetMaintenanceType.Repair, StartDate = DateTime.UtcNow,
            CompanyId = Guid.Empty,
            SnapshotSystemInfoId = sysB.Id, SnapshotSystemInfoName = sysB.Name
        });
        await ctx.SaveChangesAsync();

        var controller = new AssetMaintenancesController(ctx, new FakeCurrentUser(), new CompanyScopeFake { Super = true }, TestHelpers.CreateActionLogService(ctx));
        var result = await controller.GetAllMaintenances(assetId: null, systemInfoId: sysA.Id);

        var arr = OkData(result).EnumerateArray().ToList();
        Assert.Single(arr);
        Assert.Equal("Bảo trì hệ thống A", arr[0].GetProperty("title").GetString());
        Assert.Equal(sysA.Id, arr[0].GetProperty("snapshotSystemInfoId").GetGuid());
    }

    [Fact]
    public async Task GetAllMaintenances_WithSystemInfoId_Superuser_SeesOtherCompanyRecords()
    {
        await using var ctx = CreateContext(nameof(GetAllMaintenances_WithSystemInfoId_Superuser_SeesOtherCompanyRecords));
        var companyB = Guid.NewGuid(); // a company the superuser does NOT belong to

        var sysB = new SystemInfo { Code = "SYS-002-COR", Name = "Hệ thống B", CompanyId = companyB };
        var posB = new SystemPosition { Code = "POS-003-NOD", Name = "Vị trí khác", SystemInfo = sysB };
        ctx.SystemInfos.Add(sysB);
        ctx.SystemPositions.Add(posB);
        await ctx.SaveChangesAsync();
        var assetB = await SeedAssetAsync(ctx, posB, "AST-002");

        ctx.AssetMaintenances.Add(new AssetMaintenance
        {
            AssetId = assetB, Title = "Bảo trì công ty B",
            Type = AssetMaintenanceType.Repair, StartDate = DateTime.UtcNow,
            CompanyId = companyB, // NOT Guid.Empty — the record belongs to another company.
            SnapshotSystemInfoId = sysB.Id, SnapshotSystemInfoName = sysB.Name
        });
        await ctx.SaveChangesAsync();

        // The systemInfoId filter rides the SAME company gate as the plain list — proven by
        // GetAllMaintenances_Superuser_SeesAllCompanies (AssetMaintenanceTests): superuser →
        // GetCurrentUserCompanyIdAsync() returns null → the company filter branch is skipped.
        var controller = new AssetMaintenancesController(ctx, new FakeCurrentUser(), new CompanyScopeFake { Super = true }, TestHelpers.CreateActionLogService(ctx));
        var result = await controller.GetAllMaintenances(assetId: null, systemInfoId: sysB.Id);

        var arr = OkData(result).EnumerateArray().ToList();
        Assert.Single(arr);
        Assert.Equal("Bảo trì công ty B", arr[0].GetProperty("title").GetString());
        Assert.Equal(sysB.Id, arr[0].GetProperty("snapshotSystemInfoId").GetGuid());
    }
}
