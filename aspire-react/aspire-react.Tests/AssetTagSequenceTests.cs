using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [UI-3D] A validate-only/precheck rollback must not consume an asset-tag sequence. The generator
/// joins the caller's ambient transaction; only a real committed create advances the counter.
/// </summary>
public class AssetTagSequenceTests
{
    [ThrowawayDbFact]
    public async Task PrecheckRollback_DoesNotConsumeSequence()
    {
        var connectionString = Environment.GetEnvironmentVariable("MIRATS_TEST_POSTGRES")?.Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Missing MIRATS_TEST_POSTGRES (temporary DB connection string).");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("aspire-react.Server"))
            .Options;
        await using var db = new AppDbContext(options, new TestHelpers.FakeScope { Super = true });
        await ThrowawayDbMigrator.MigrateOnceAsync(db, connectionString);

        var company = new Company { Code = "SEQ" + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant(), Name = "Sequence test" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var generator = new AssetTagGenerator(db);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var previewTag = await generator.ResolveAssetTagAsync(null, company.Id);
            Assert.EndsWith("-001", previewTag);
            await tx.RollbackAsync();
        }

        var realTag = await generator.ResolveAssetTagAsync(null, company.Id);
        Assert.EndsWith("-001", realTag);
    }
}
