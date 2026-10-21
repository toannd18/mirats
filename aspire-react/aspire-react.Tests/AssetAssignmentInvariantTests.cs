using aspire_react.Server.Application.Assets.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [DASHBOARD-DATA] Production invariant for a checkout: an asset with a CurrentAssignmentId must
/// be Deployed. Runs against real Postgres because CheckoutAssetCommand uses FOR UPDATE + a real
/// transaction; InMemory cannot exercise that path.
/// </summary>
public class AssetAssignmentInvariantTests
{
    [ThrowawayDbFact]
    public async Task Checkout_AssignmentAndStatus_StayInSync_OnRealPostgres()
    {
        var connectionString = Environment.GetEnvironmentVariable("MIRATS_TEST_POSTGRES")?.Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Thiếu MIRATS_TEST_POSTGRES.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("aspire-react.Server"))
            .Options;
        await using (var setup = new AppDbContext(options, new TestHelpers.SuperUserScope()))
        {
            await ThrowawayDbMigrator.MigrateOnceAsync(setup, connectionString);
        }

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var company = new Company { Name = "INV-" + suffix, Code = "INV-" + suffix };
        var actor = new User
        {
            Username = "qa-inv-" + suffix,
            Email = $"qa-inv-{suffix}@local",
            FirstName = "QA",
            LastName = "Invariant",
            CompanyId = company.Id,
        };
        var assignee = new User
        {
            Username = "qa-inv-target-" + suffix,
            Email = $"qa-inv-target-{suffix}@local",
            FirstName = "QA",
            LastName = "Target",
            CompanyId = company.Id,
        };
        var asset = new Asset
        {
            AssetTag = "INV-" + suffix,
            Name = "Invariant asset",
            CompanyId = company.Id,
            IsConfirmed = true,
            Status = AssetStatus.Pending,
        };

        await using (var setup = new AppDbContext(options, new TestHelpers.SuperUserScope()))
        {
            setup.AddRange(company, actor, assignee, asset);
            await setup.SaveChangesAsync();
        }

        await using var db = new AppDbContext(options, new TestHelpers.SuperUserScope());
        var handler = new CheckoutAssetCommandHandler(db, TestHelpers.CreateActionLogService(db, actor.Id));
        var result = await handler.Handle(new CheckoutAssetCommand(
            asset.Id, AssignmentTargetType.User, assignee.Id, "invariant", null, null, actor.Id), default);

        Assert.True(result.Success, result.Message);
        db.ChangeTracker.Clear();
        var saved = await db.Assets.AsNoTracking().SingleAsync(a => a.Id == asset.Id);
        Assert.NotNull(saved.CurrentAssignmentId);
        Assert.Equal(AssetStatus.Deployed, saved.Status);
    }
}
