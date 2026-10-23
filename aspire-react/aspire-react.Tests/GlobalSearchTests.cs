using aspire_react.Server.Application.Search;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [UI-3C] Global search is tested against PostgreSQL because unaccent() is a database extension.
/// Covers unaccented matching, exact/prefix/contains priority, per-type limits, soft-deleted
/// licenses, company scope, and the users.view permission switch.
/// </summary>
public class GlobalSearchTests
{
    public const string ConnectionStringEnv = "MIRATS_TEST_POSTGRES";

    [ThrowawayDbFact]
    public async Task Search_UnaccentPriorityLimitScopeSoftDeleteAndPermissions()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnv)?.Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"Missing {ConnectionStringEnv} (temporary DB connection string).");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("aspire-react.Server"))
            .Options;
        var scope = new TestHelpers.FakeScope { Super = true };
        await using var db = new AppDbContext(options, scope);
        await ThrowawayDbMigrator.MigrateOnceAsync(db, connectionString);

        var tag = "GS-" + Guid.NewGuid().ToString("N")[..8];
        var co1 = new Company { Code = tag + "A", Name = "Search Co A" };
        var co2 = new Company { Code = tag + "B", Name = "Search Co B" };
        db.AddRange(co1, co2);
        await db.SaveChangesAsync();

        Asset AddAsset(string suffix, string name, Guid? companyId) => new()
        {
            AssetTag = $"{tag}-{suffix}",
            Name = name,
            CompanyId = companyId,
        };
        db.Assets.AddRange(
            AddAsset("MAY-IN", "Máy in exact", co1.Id),
            AddAsset("MAY-IN-2", "Máy in prefix", co1.Id),
            AddAsset("ZZ-MAY-IN", "Máy in contains", co1.Id),
            AddAsset("ZZ-MAY-IN-1", "Máy in extra 1", co1.Id),
            AddAsset("ZZ-MAY-IN-2", "Máy in extra 2", co1.Id),
            AddAsset("ZZ-MAY-IN-3", "Máy in extra 3", co1.Id),
            AddAsset("CO-B-MAY-IN", "Company B May in", co2.Id));

        var deletedLicense = new License
        {
            Name = "May in deleted",
            Serial = "DELETED-" + tag,
            CompanyId = co1.Id,
            DeletedAt = DateTime.UtcNow,
        };
        db.Licenses.Add(deletedLicense);
        db.SystemInfos.Add(new SystemInfo { Code = tag + "-SYS", Name = "Máy in system", CompanyId = co1.Id });
        db.Consumables.Add(new Consumable { Name = "Máy in consumable", ItemNo = "VT-" + tag, CompanyId = co1.Id });
        db.Users.Add(new User
        {
            Username = "may-in-" + tag,
            FirstName = "Máy",
            LastName = "In",
            Email = "may-in-" + tag + "@test.local",
            CompanyId = co1.Id,
            IsActive = true,
        });
        await db.SaveChangesAsync();

        var handler = new GlobalSearchQueryHandler(db, scope);
        var all = new SearchPermissions(true, true, true, true, true, true, true);

        // "may in" must match "Máy in" (unaccent), and the exact/prefix code wins over contains.
        var unaccent = await handler.Handle(new GlobalSearchQuery("may in", all), default);
        var assets = Assert.Single(unaccent.Groups, g => g.Type == "asset").Items;
        Assert.Equal(5, assets.Count);
        Assert.True(unaccent.HasMore);
        Assert.Contains(unaccent.Groups, g => g.Type == "system");
        Assert.Contains(unaccent.Groups, g => g.Type == "consumable");
        Assert.Contains(unaccent.Groups, g => g.Type == "user");

        // Exact tag > prefix tag > contains for the same term.
        var priority = await handler.Handle(new GlobalSearchQuery($"{tag}-MAY-IN", all), default);
        var priorityAssets = Assert.Single(priority.Groups, g => g.Type == "asset").Items;
        Assert.Equal($"{tag}-MAY-IN", priorityAssets[0].Title);
        Assert.Equal($"{tag}-MAY-IN-2", priorityAssets[1].Title);

        // Deleted licenses are excluded even when the text matches.
        var deleted = await handler.Handle(new GlobalSearchQuery("deleted", all), default);
        Assert.DoesNotContain(deleted.Groups, g => g.Type == "license");

        // Permission switch: users are omitted without users.view.
        var withoutUsers = await handler.Handle(
            new GlobalSearchQuery("may in", new SearchPermissions(true, true, true, true, true, true, false)),
            default);
        Assert.DoesNotContain(withoutUsers.Groups, g => g.Type == "user");

        // Company scope: regular user sees own company + company-less rows, never Company B.
        scope.Super = false;
        scope.CompanyId = co1.Id;
        var scoped = await handler.Handle(new GlobalSearchQuery("company b", all), default);
        Assert.DoesNotContain(scoped.Groups.SelectMany(g => g.Items), i => i.Id == db.Assets.Local
            .Single(a => a.AssetTag == $"{tag}-CO-B-MAY-IN").Id);
    }
}
