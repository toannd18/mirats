using aspire_react.Server.Application.Reports.Queries;
using aspire_react.Server.Application.Reports.RepairReports;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [UI-3D] Report contract tests: VN-calendar PurchaseDate filtering (timestamptz), server-side
/// depreciation pagination and the custom-report truncation flag.
/// </summary>
public class ReportQueryTests
{
    [Fact]
    public void PurchaseDate_Instant_MapsToVnCalendarYear()
    {
        // 2025-01-01 00:00 VN == 2024-12-31 17:00 UTC.
        Assert.Equal(new DateOnly(2025, 1, 1), VnReportDate.ToVnDateFromInstant(
            DateTime.SpecifyKind(new DateTime(2024, 12, 31, 17, 0, 0), DateTimeKind.Utc)));
        Assert.Equal(new DateTime(2024, 12, 31, 17, 0, 0, DateTimeKind.Utc),
            VnReportDate.YearRangeUtc(2025).StartUtc);
    }

    [ThrowawayDbFact]
    public async Task Reports_VnYearPaginationAndCustomTruncation()
    {
        var connectionString = Environment.GetEnvironmentVariable("MIRATS_TEST_POSTGRES")?.Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Missing MIRATS_TEST_POSTGRES (temporary DB connection string).");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("aspire-react.Server"))
            .Options;
        var tag = "RQ-" + Guid.NewGuid().ToString("N")[..8];
        var company = new Company { Code = tag, Name = "Report Co " + tag };
        var category = new Category { Name = "Report Cat " + tag, CategoryType = CategoryType.Asset };
        var depreciation = new Depreciation { Name = "Report Dep " + tag, Months = 12 };
        var model = new AssetModel { Name = "Report Model " + tag, CategoryId = category.Id, DepreciationId = depreciation.Id };
        var scope = new TestHelpers.FakeScope { Super = false, CompanyId = company.Id };
        await using var db = new AppDbContext(options, scope);
        await ThrowawayDbMigrator.MigrateOnceAsync(db, connectionString);
        db.AddRange(company, category, depreciation, model);
        await db.SaveChangesAsync();

        var yearStart = DateTime.SpecifyKind(new DateTime(2025, 1, 1, 0, 0, 0), DateTimeKind.Utc);
        var in2025 = Enumerable.Range(0, 21).Select(i => new Asset
        {
            AssetTag = $"{tag}-Y-{i:00}",
            Name = $"Year asset {i}",
            CompanyId = company.Id,
            ModelId = model.Id,
            PurchaseCost = 1200,
            PurchaseDate = yearStart.AddDays(i),
        }).ToList();
        var boundary = new Asset
        {
            AssetTag = tag + "-BOUNDARY",
            Name = "Boundary asset",
            CompanyId = company.Id,
            ModelId = model.Id,
            PurchaseCost = 1200,
            PurchaseDate = DateTime.SpecifyKind(new DateTime(2024, 12, 31, 17, 0, 0), DateTimeKind.Utc),
        };
        var previous = new Asset
        {
            AssetTag = tag + "-PREVIOUS",
            Name = "Previous asset",
            CompanyId = company.Id,
            ModelId = model.Id,
            PurchaseCost = 1200,
            PurchaseDate = DateTime.SpecifyKind(new DateTime(2024, 12, 31, 16, 59, 0), DateTimeKind.Utc),
        };
        db.Assets.AddRange(in2025);
        db.Assets.AddRange(boundary, previous);
        await db.SaveChangesAsync();

        var dep = new DepreciationReportQueryHandler(db, scope);
        var page1 = await dep.Handle(new DepreciationReportQuery(1, 20, company.Id, null, 2025), default);
        var page2 = await dep.Handle(new DepreciationReportQuery(2, 20, company.Id, null, 2025), default);
        var year2024 = await dep.Handle(new DepreciationReportQuery(1, 20, company.Id, null, 2024), default);
        Assert.Equal(22, page1.Total);
        Assert.Equal(20, page1.Items.Count);
        Assert.True(page1.HasMore);
        Assert.Equal(2, page2.Items.Count);
        Assert.False(page2.HasMore);
        Assert.Equal(1, year2024.Total);
        Assert.Contains(year2024.Items, item => item.AssetTag == tag + "-PREVIOUS");
        Assert.DoesNotContain(year2024.Items, item => item.AssetTag == tag + "-BOUNDARY");

        var audit = new AuditReportQueryHandler(db, scope);
        var audit2025 = await audit.Handle(new AuditReportQuery(company.Id, null, 2025), default);
        Assert.Equal(22, audit2025.TotalAssets);
        Assert.Equal(22, audit2025.NotAudited);

        // 501 assets exercise the server-side custom-report cap and the explicit truncated flag.
        var bulk = Enumerable.Range(0, 501).Select(i => new Asset
        {
            AssetTag = $"{tag}-BULK-{i:000}",
            Name = $"Bulk asset {i}",
            CompanyId = company.Id,
        });
        db.Assets.AddRange(bulk);
        await db.SaveChangesAsync();

        var custom = await new CustomReportQueryHandler(db, scope)
            .Handle(new CustomReportQuery(null, null, null, null, null), default);
        Assert.Equal(500, custom.Items.Count);
        Assert.True(custom.Total >= 501);
        Assert.True(custom.Truncated);
    }
}
