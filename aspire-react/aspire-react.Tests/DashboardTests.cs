using System.Text.Json;
using aspire_react.Server.Application.Dashboard.Queries;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// MC-4 — Dashboard summary: systemsOverdueMaintenance counts SystemInfos whose
/// NextMaintenanceDueDate is in the past, within the user's company scope (own company + floater;
/// superuser sees all). Mirrors the overdueAudits/lowStockCount pattern.
/// [Giai đoạn 2-cuối] Dashboard migrated to MediatR — tests now drive the summary Query handler
/// directly with FakeScope (same scope substance as the old controller-level tests).
/// </summary>
public class DashboardTests
{
    private static GetDashboardSummaryQueryHandler Ctx(AppDbContext db, bool super, Guid? companyId)
        => new(db, new TestHelpers.FakeScope { Super = super, CompanyId = companyId });

    private static int ReadSystemsOverdue(DashboardSummaryDto dto)
        => dto.SystemsOverdueMaintenance;

    private static async Task<DashboardSummaryDto> RunSummary(AppDbContext db, bool super, Guid? companyId)
    {
        var handler = Ctx(db, super, companyId);
        return await handler.Handle(new GetDashboardSummaryQuery(), CancellationToken.None);
    }

    [Fact]
    public async Task Summary_CountsOnlyOverdueSystems_InUserCompanyScope()
    {
        await using var db = TestHelpers.CreateContext(nameof(Summary_CountsOnlyOverdueSystems_InUserCompanyScope));
        var coA = new Company { Code = "DA", Name = "Cty A" };
        var coB = new Company { Code = "DB", Name = "Cty B" };
        var now = DateTime.UtcNow;

        // Company A: 2 overdue + 1 future + 1 never-maintained (null).
        db.SystemInfos.AddRange(
            new SystemInfo { Code = "S1", Name = "A quá hạn 1", CompanyId = coA.Id, NextMaintenanceDueDate = now.AddDays(-30) },
            new SystemInfo { Code = "S2", Name = "A quá hạn 2", CompanyId = coA.Id, NextMaintenanceDueDate = now.AddHours(-1) },
            new SystemInfo { Code = "S3", Name = "A còn hạn", CompanyId = coA.Id, NextMaintenanceDueDate = now.AddDays(10) },
            new SystemInfo { Code = "S4", Name = "A chưa bảo dưỡng", CompanyId = coA.Id, NextMaintenanceDueDate = null },
            // Company B: 1 overdue — must NOT leak into company A's scope.
            new SystemInfo { Code = "S5", Name = "B quá hạn", CompanyId = coB.Id, NextMaintenanceDueDate = now.AddDays(-5) },
            // Floater: 1 overdue — visible to any regular user.
            new SystemInfo { Code = "S6", Name = "floater quá hạn", CompanyId = null, NextMaintenanceDueDate = now.AddDays(-2) });
        db.Companies.AddRange(coA, coB);
        await db.SaveChangesAsync();

        // User of company A → sees own overdue (2) + floater overdue (1) = 3.
        var dtoA = await RunSummary(db, false, coA.Id);
        Assert.Equal(3, ReadSystemsOverdue(dtoA));

        // User of company B → sees own overdue (1) + floater overdue (1) = 2.
        var dtoB = await RunSummary(db, false, coB.Id);
        Assert.Equal(2, ReadSystemsOverdue(dtoB));

        // Superuser → all overdue systems (2 A + 1 B + 1 floater = 4).
        var dtoSuper = await RunSummary(db, true, null);
        Assert.Equal(4, ReadSystemsOverdue(dtoSuper));

        // Company-less regular user (Guid.Empty sentinel) → only floater (1).
        var companyless = await RunSummary(db, false, null);
        Assert.Equal(1, ReadSystemsOverdue(companyless));
    }

    [Fact]
    public async Task Summary_FieldPresent_AlongsideExistingCounters()
    {
        await using var db = TestHelpers.CreateContext(nameof(Summary_FieldPresent_AlongsideExistingCounters));
        var dto = await RunSummary(db, true, null);

        // New field exists AND the pre-existing counters are untouched.
        Assert.True(dto.SystemsOverdueMaintenance >= 0);
        Assert.True(dto.LowStockCount >= 0);
        Assert.True(dto.OverdueAudits >= 0);
        Assert.True(dto.TotalAssets >= 0);
    }

    // ==================== [BUG-J FIX] monthly-checkout-trend ====================

    private static GetMonthlyCheckoutTrendQueryHandler TrendCtx(AppDbContext db, bool super, Guid? companyId)
        => new(db, new TestHelpers.FakeScope { Super = super, CompanyId = companyId });

    [Fact]
    public async Task Trend_Superuser_ReturnsData_Previously500_BugRepro()
    {
        await using var db = TestHelpers.CreateContext(nameof(Trend_Superuser_ReturnsData_Previously500_BugRepro));
        var now = DateTime.UtcNow;
        db.ActionLogs.AddRange(
            new ActionLog { ItemType = ItemType.Asset, ItemId = Guid.NewGuid(), ActionType = ActionType.Checkout, CreatedBy = Guid.NewGuid(), ActionDate = now.AddDays(-1) },
            new ActionLog { ItemType = ItemType.Asset, ItemId = Guid.NewGuid(), ActionType = ActionType.Checkin, CreatedBy = Guid.NewGuid(), ActionDate = now.AddDays(-1) },
            new ActionLog { ItemType = ItemType.Asset, ItemId = Guid.NewGuid(), ActionType = ActionType.Checkout, CreatedBy = Guid.NewGuid(), ActionDate = now.AddDays(-40) });
        await db.SaveChangesAsync();

        // BUG-J reproduction: superuser (scope null) previously threw ArgumentNullException
        // during EF translation → 500. Now: 200 + aggregated rows.
        var rows = await TrendCtx(db, true, null).Handle(new GetMonthlyCheckoutTrendQuery(), CancellationToken.None);

        Assert.NotNull(rows);
        Assert.Equal(2, rows.Count); // 2 distinct months
        var thisMonth = rows.Single(r => r.Month == $"{now.Year}-{now.Month:D2}");
        Assert.Equal(1, thisMonth.CheckoutCount);
        Assert.Equal(1, thisMonth.CheckinCount);
    }

    [Fact]
    public async Task Trend_RegularUser_ScopesToCompanyAndFloaters()
    {
        await using var db = TestHelpers.CreateContext(nameof(Trend_RegularUser_ScopesToCompanyAndFloaters));
        var coA = new Company { Code = "TA", Name = "Cty A" };
        var coB = new Company { Code = "TB", Name = "Cty B" };
        db.Companies.AddRange(coA, coB);
        var assetA = new Asset { AssetTag = "A1", Name = "A1", CompanyId = coA.Id };
        var assetB = new Asset { AssetTag = "B1", Name = "B1", CompanyId = coB.Id };
        db.Assets.AddRange(assetA, assetB);
        await db.SaveChangesAsync();
        var now = DateTime.UtcNow;
        db.ActionLogs.AddRange(
            new ActionLog { ItemType = ItemType.Asset, ItemId = assetA.Id, ActionType = ActionType.Checkout, CreatedBy = Guid.NewGuid(), ActionDate = now.AddDays(-1) },
            new ActionLog { ItemType = ItemType.Asset, ItemId = assetB.Id, ActionType = ActionType.Checkout, CreatedBy = Guid.NewGuid(), ActionDate = now.AddDays(-1) });
        await db.SaveChangesAsync();

        // Regular user of company A: sees own asset's checkout only — company B's must not leak.
        var rows = await TrendCtx(db, false, coA.Id).Handle(new GetMonthlyCheckoutTrendQuery(), CancellationToken.None);

        Assert.Single(rows);
        Assert.Equal(1, rows[0].CheckoutCount);
    }
}
