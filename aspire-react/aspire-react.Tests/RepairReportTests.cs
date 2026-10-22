using aspire_react.Server.Application.Reports.RepairReports;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

public class RepairReportTests
{
    public const string ConnectionStringEnv = "MIRATS_TEST_POSTGRES";

    [Fact]
    public void VnReportDate_Boundary_31Oct1730Utc_Is01NovVn()
    {
        Assert.Equal(new DateOnly(2026, 11, 1),
            VnReportDate.ToVnDate(DateTime.SpecifyKind(new DateTime(2026, 10, 31, 17, 30, 0), DateTimeKind.Utc)));
        Assert.Equal(new DateOnly(2027, 1, 1),
            VnReportDate.ToVnDate(DateTime.SpecifyKind(new DateTime(2026, 12, 31, 17, 30, 0), DateTimeKind.Utc)));
        Assert.Equal(new DateTime(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc),
            VnReportDate.StartOfDayUtc(new DateOnly(2026, 11, 1)));
    }

    [ThrowawayDbFact]
    public async Task RepairReport_AggregatesSql_SystemMappingDateBoundaryAndScope()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnv)?.Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"Missing {ConnectionStringEnv} (temporary DB connection string).");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("aspire-react.Server"))
            .Options;
        var scope = new TestHelpers.FakeScope { Super = false };
        await using var db = new AppDbContext(options, scope);
        await ThrowawayDbMigrator.MigrateOnceAsync(db, connectionString);

        var tag = "RR-" + Guid.NewGuid().ToString("N")[..8];
        var co1 = new Company { Code = tag + "A", Name = "Repair Co A" };
        var co2 = new Company { Code = tag + "B", Name = "Repair Co B" };
        var cat = new Category { Name = "Repair Cat " + tag, CategoryType = CategoryType.Asset };
        var model = new AssetModel { Name = "Repair Model " + tag, CategoryId = cat.Id };
        var sysA = new SystemInfo { Code = $"RRA-2026-{tag[^3..]}", Name = "Repair System A", CompanyId = co1.Id };
        var sysB = new SystemInfo { Code = $"RRB-2026-{tag[^3..]}", Name = "Repair System B", CompanyId = co1.Id };
        var a1 = new Asset { AssetTag = tag + "-A1", Name = "Asset A1", CompanyId = co1.Id, ModelId = model.Id };
        var a2 = new Asset { AssetTag = tag + "-A2", Name = "Asset A2", CompanyId = co1.Id, ModelId = model.Id };
        var a3 = new Asset { AssetTag = tag + "-A3", Name = "Asset A3", CompanyId = co1.Id, ModelId = model.Id };
        var a4 = new Asset { AssetTag = tag + "-A4", Name = "Asset A4", CompanyId = co1.Id, ModelId = model.Id };
        var a5 = new Asset { AssetTag = tag + "-A5", Name = "Asset A5", CompanyId = co2.Id, ModelId = model.Id };
        db.AddRange(co1, co2, cat, model, sysA, sysB, a1, a2, a3, a4, a5);
        await db.SaveChangesAsync();

        var utc = DateTimeKind.Utc;
        db.AssetInstallations.AddRange(
            new AssetInstallation
            {
                AssetId = a1.Id,
                SystemInfoId = sysA.Id,
                SystemInfoName = sysA.Name,
                StartedAt = DateTime.SpecifyKind(new DateTime(2026, 9, 1), utc),
                CreatedAt = DateTime.SpecifyKind(new DateTime(2026, 9, 1), utc),
                UpdatedAt = DateTime.SpecifyKind(new DateTime(2026, 9, 1), utc),
            },
            new AssetInstallation
            {
                AssetId = a3.Id,
                SystemInfoId = sysA.Id,
                SystemInfoName = sysA.Name,
                StartedAt = DateTime.SpecifyKind(new DateTime(2026, 9, 1), utc),
                EndedAt = DateTime.SpecifyKind(new DateTime(2026, 10, 10), utc),
                CreatedAt = DateTime.SpecifyKind(new DateTime(2026, 9, 1), utc),
                UpdatedAt = DateTime.SpecifyKind(new DateTime(2026, 10, 10), utc),
            },
            new AssetInstallation
            {
                AssetId = a3.Id,
                SystemInfoId = sysB.Id,
                SystemInfoName = sysB.Name,
                StartedAt = DateTime.SpecifyKind(new DateTime(2026, 10, 10), utc),
                CreatedAt = DateTime.SpecifyKind(new DateTime(2026, 10, 10), utc),
                UpdatedAt = DateTime.SpecifyKind(new DateTime(2026, 10, 10), utc),
            },
            new AssetInstallation
            {
                AssetId = a4.Id,
                SystemInfoId = sysA.Id,
                SystemInfoName = sysA.Name,
                StartedAt = DateTime.SpecifyKind(new DateTime(2026, 10, 15, 1, 0, 0), utc),
                EndedAt = DateTime.SpecifyKind(new DateTime(2026, 10, 15, 2, 0, 0), utc),
                CreatedAt = DateTime.SpecifyKind(new DateTime(2026, 10, 15), utc),
                UpdatedAt = DateTime.SpecifyKind(new DateTime(2026, 10, 15), utc),
            },
            new AssetInstallation
            {
                AssetId = a4.Id,
                SystemInfoId = sysB.Id,
                SystemInfoName = sysB.Name,
                StartedAt = DateTime.SpecifyKind(new DateTime(2026, 10, 15, 3, 0, 0), utc),
                CreatedAt = DateTime.SpecifyKind(new DateTime(2026, 10, 15), utc),
                UpdatedAt = DateTime.SpecifyKind(new DateTime(2026, 10, 15), utc),
            });
        await db.SaveChangesAsync();

        AssetMaintenance M(Guid assetId, Guid companyId, AssetMaintenanceType type, int month, int day,
            int? completionDay = null, decimal? cost = null, bool warranty = false, bool deleted = false,
            int hour = 0, int minute = 0)
            => new()
            {
                AssetId = assetId,
                CompanyId = companyId,
                Type = type,
                Title = "Repair fixture " + tag,
                StartDate = DateTime.SpecifyKind(new DateTime(2026, month, day, hour, minute, 0), utc),
                CompletionDate = completionDay.HasValue
                    ? DateTime.SpecifyKind(new DateTime(2026, month, completionDay.Value, 10, 0, 0), utc)
                    : null,
                Cost = cost,
                IsWarranty = warranty,
                CreatedById = Guid.NewGuid(),
                DeletedAt = deleted ? DateTime.SpecifyKind(new DateTime(2026, 10, 1), utc) : null,
            };

        db.AssetMaintenances.AddRange(
            M(a1.Id, co1.Id, AssetMaintenanceType.Repair, 10, 5, 7, 100m, true),       // at-start, completed, warranty
            M(a1.Id, co1.Id, AssetMaintenanceType.IncidentReport, 10, 20),             // at-start, open
            M(a2.Id, co1.Id, AssetMaintenanceType.Repair, 10, 3),                      // unassigned
            M(a3.Id, co1.Id, AssetMaintenanceType.Repair, 10, 5, 7),                   // sys A before move
            M(a3.Id, co1.Id, AssetMaintenanceType.Repair, 10, 20, 25),                 // sys B after move
            M(a4.Id, co1.Id, AssetMaintenanceType.Repair, 10, 15),                     // invalid: 2 same-day rows
            M(a1.Id, co1.Id, AssetMaintenanceType.Repair, 10, 31, hour: 17, minute: 30), // VN 01/11
            M(a1.Id, co1.Id, AssetMaintenanceType.Maintenance, 10, 2),                 // excluded by default group
            M(a1.Id, co1.Id, AssetMaintenanceType.Repair, 10, 9, deleted: true),       // soft-deleted
            M(a5.Id, co2.Id, AssetMaintenanceType.Repair, 10, 11));                   // other company
        await db.SaveChangesAsync();

        scope.Super = true;
        var handler = new RepairReportQueryHandler(db, scope);
        var october = await handler.Handle(new RepairReportQuery(
            "month", 2026, 10, null, null, false, "repair_incident", cat.Id, model.Id), default);

        Assert.Equal(7, october.Summary.Failures);
        Assert.Equal(4, october.Summary.OpenCount);
        Assert.Equal(3, october.Summary.CompletedCount);
        Assert.Equal(3.0m, october.Summary.MttrDays);
        Assert.Equal(100m, october.Summary.TotalCost);
        Assert.Equal(6, october.Summary.MissingCostCount);
        Assert.Equal(1, october.Summary.WarrantyCount);
        Assert.Equal(2, october.Summary.UnassignedCount);
        Assert.Equal(1, october.Summary.InvalidInstallationCount);
        Assert.Contains(october.Groups, g => g.SystemName == sysA.Name && g.Failures == 3);
        Assert.Contains(october.Groups, g => g.SystemName == sysB.Name && g.Failures == 1);
        Assert.Contains(october.Items, i => i.AssetId == a3.Id && i.SystemName == sysA.Name);
        Assert.Contains(october.Items, i => i.AssetId == a3.Id && i.SystemName == sysB.Name);
        Assert.DoesNotContain(october.Items, i => i.Type == nameof(AssetMaintenanceType.Maintenance));

        var onlySystemA = await handler.Handle(new RepairReportQuery(
            "month", 2026, 10, null, sysA.Id, false, "repair_incident", cat.Id, model.Id), default);
        Assert.Equal(3, onlySystemA.Summary.Failures);

        var onlyUnassigned = await handler.Handle(new RepairReportQuery(
            "month", 2026, 10, null, null, true, "repair_incident", cat.Id, model.Id), default);
        Assert.Equal(2, onlyUnassigned.Summary.Failures);

        var november = await handler.Handle(new RepairReportQuery(
            "month", 2026, 11, null, null, false, "repair_incident", cat.Id, model.Id), default);
        Assert.Equal(1, november.Summary.Failures);
        Assert.Single(november.Items);

        var allTypes = await handler.Handle(new RepairReportQuery(
            "month", 2026, 10, null, null, false, "all", cat.Id, model.Id), default);
        Assert.Equal(8, allTypes.Summary.Failures);

        scope.Super = false;
        scope.CompanyId = co1.Id;
        var scoped = await handler.Handle(new RepairReportQuery(
            "month", 2026, 10, null, null, false, "repair_incident", cat.Id, model.Id), default);
        Assert.Equal(6, scoped.Summary.Failures);
        Assert.DoesNotContain(scoped.Items, i => i.AssetId == a5.Id);
    }
}
