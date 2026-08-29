using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using aspire_react.Server.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// MC-4 — DashboardController.GetSummary: systemsOverdueMaintenance counts SystemInfos whose
/// NextMaintenanceDueDate is in the past, within the user's company scope (own company + floater;
/// superuser sees all). Mirrors the overdueAudits/lowStockCount pattern.
/// </summary>
public class DashboardTests
{
    private sealed class NoopLogVisibility : IActionLogVisibilityService
    {
        public Task<List<ActionLog>> FilterVisibleLogsAsync(IReadOnlyList<ActionLog> logs, Guid userCompanyId)
            => Task.FromResult(logs.ToList());
    }

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static DashboardController Ctx(AppDbContext db, bool super, Guid? companyId)
        => new(db, new TestHelpers.FakeScope { Super = super, CompanyId = companyId }, new NoopLogVisibility());

    private static int ReadInt(object? value, string prop)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value, WebJson));
        return doc.RootElement.GetProperty("data").GetProperty(prop).GetInt32();
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
        var userA = await Ctx(db, false, coA.Id).GetSummary();
        var okA = Assert.IsType<OkObjectResult>(userA);
        Assert.Equal(3, ReadInt(okA.Value, "systemsOverdueMaintenance"));

        // User of company B → sees own overdue (1) + floater overdue (1) = 2.
        var userB = await Ctx(db, false, coB.Id).GetSummary();
        Assert.Equal(2, ReadInt(Assert.IsType<OkObjectResult>(userB).Value, "systemsOverdueMaintenance"));

        // Superuser → all overdue systems (2 A + 1 B + 1 floater = 4).
        var super = await Ctx(db, true, null).GetSummary();
        Assert.Equal(4, ReadInt(Assert.IsType<OkObjectResult>(super).Value, "systemsOverdueMaintenance"));

        // Company-less regular user (Guid.Empty sentinel) → only floater (1).
        var companyless = await Ctx(db, false, null).GetSummary();
        Assert.Equal(1, ReadInt(Assert.IsType<OkObjectResult>(companyless).Value, "systemsOverdueMaintenance"));
    }

    [Fact]
    public async Task Summary_FieldPresent_AlongsideExistingCounters()
    {
        await using var db = TestHelpers.CreateContext(nameof(Summary_FieldPresent_AlongsideExistingCounters));
        var controller = Ctx(db, true, null);
        var result = await controller.GetSummary();
        var ok = Assert.IsType<OkObjectResult>(result);

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, WebJson));
        var data = doc.RootElement.GetProperty("data");
        // New field exists AND the pre-existing counters are untouched.
        Assert.True(data.TryGetProperty("systemsOverdueMaintenance", out _));
        Assert.True(data.TryGetProperty("lowStockCount", out _));
        Assert.True(data.TryGetProperty("overdueAudits", out _));
        Assert.True(data.TryGetProperty("totalAssets", out _));
    }
}
