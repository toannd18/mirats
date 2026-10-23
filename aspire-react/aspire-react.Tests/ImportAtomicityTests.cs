using aspire_react.Server.Application.ImportExport;
using aspire_react.Server.Application.ImportExport.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [UI-3C] Precheck/atomic import contract: a validate-only call must never persist rows, and a
/// failed real import must roll the whole batch back. Uses a stub mediator because the transaction
/// boundary lives in the controller, not in the existing per-row Excel service.
/// </summary>
public class ImportAtomicityTests
{
    [ThrowawayDbFact]
    public async Task Import_PrecheckAndFailure_RollBackEverything()
    {
        var connectionString = Environment.GetEnvironmentVariable("MIRATS_TEST_POSTGRES")?.Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Missing MIRATS_TEST_POSTGRES (temporary DB connection string).");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("aspire-react.Server"))
            .Options;
        await using var db = new AppDbContext(options, new TestHelpers.FakeScope { Super = true });
        await ThrowawayDbMigrator.MigrateOnceAsync(db, connectionString);

        var categoryName = "Import preview " + Guid.NewGuid().ToString("N")[..8];
        var stub = new TestHelpers.StubMediator(request =>
        {
            if (request is not ImportAssetsCommand)
                throw new InvalidOperationException("Unexpected request type.");

            db.Categories.Add(new Category
            {
                Name = categoryName,
                CategoryType = aspire_react.Server.Domain.Enums.CategoryType.Asset,
            });
            db.SaveChanges();
            return new ImportOutcome(true, Result: new ImportSheetResult(
                1, 1,
                new[] { new ImportRowResult(1, true, "OK"), new ImportRowResult(2, false, "Lỗi dữ liệu") },
                new[] { new ImportRowResult(2, false, "Lỗi dữ liệu") }));
        });
        var controller = new ImportExportController(
            stub, db, new TestHelpers.FakeScope { Super = true }, TestHelpers.CreateActionLogService(db));

        var preview = await controller.ImportAssets(null, Guid.NewGuid(), validateOnly: true);
        Assert.IsType<OkObjectResult>(preview);
        Assert.Equal(0, await db.Categories.CountAsync(c => c.Name == categoryName));

        var real = await controller.ImportAssets(null, Guid.NewGuid(), validateOnly: false);
        Assert.IsType<OkObjectResult>(real);
        Assert.Equal(0, await db.Categories.CountAsync(c => c.Name == categoryName));
    }
}
