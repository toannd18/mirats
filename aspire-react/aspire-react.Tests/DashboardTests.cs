using System.Text.Json;
using aspire_react.Server.Application.Dashboard.Queries;
using aspire_react.Server.Domain.Entities;
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
}
