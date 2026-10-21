using aspire_react.Server.Application.Assets.Queries;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// UI-1 GD3 asset list sort:
/// - default order is newest-created first;
/// - sorting by AssetTag uses PostgreSQL ICU numeric collation so "-2" precedes "-10".
/// </summary>
public class AssetListSortTests
{
    public const string ConnectionStringEnv = "MIRATS_TEST_POSTGRES";

    [Fact]
    public void AssetTagSort_UsesIcuNumericCollation_OnNpgsql()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=canary;Username=canary;Password=canary")
            .Options;
        using var db = new AppDbContext(options, new TestHelpers.SuperUserScope());

        var sql = AssetListSort.Apply(db.Assets.AsNoTracking(), "assettag", false).ToQueryString();
        Assert.Contains("COLLATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(AssetListSort.NaturalCollation, sql);

        var defaultSql = AssetListSort.Apply(db.Assets.AsNoTracking(), null, false).ToQueryString();
        Assert.Contains("CreatedAt", defaultSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DESC", defaultSql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Real-Postgres regression, guarded like the other throwaway-DB tests. Run with
    /// MIRATS_TEST_THROWAWAY_DB=1 and MIRATS_TEST_POSTGRES pointing at a disposable database.
    /// </summary>
    [ThrowawayDbFact]
    public async Task AssetTagSort_NaturalOrder_PutsMinus2BeforeMinus10_OnRealPostgres()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnv)?.Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"Thiếu {ConnectionStringEnv} (connection string tới DB TẠM).");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("aspire-react.Server"))
            .Options;
        await using var db = new AppDbContext(options, new TestHelpers.SuperUserScope());
        await ThrowawayDbMigrator.MigrateOnceAsync(db, connectionString);

        var prefix = "UISORT-" + Guid.NewGuid().ToString("N")[..8];
        db.Assets.AddRange(
            new Asset { AssetTag = $"{prefix}-10", Name = "sort ten" },
            new Asset { AssetTag = $"{prefix}-2", Name = "sort two" });
        await db.SaveChangesAsync();

        var ordered = await AssetListSort.Apply(
                db.Assets.AsNoTracking().Where(a => a.AssetTag.StartsWith(prefix)),
                "assettag",
                false)
            .Select(a => a.AssetTag)
            .ToListAsync();

        Assert.Equal(new[] { $"{prefix}-2", $"{prefix}-10" }, ordered);
    }
}
