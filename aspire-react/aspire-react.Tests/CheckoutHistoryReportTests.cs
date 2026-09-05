using aspire_react.Server.Application.Reports.Queries;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [BUG-L FIX] Reports checkout-history date filters — behavior change approved (500 → 200 with
/// filters). The REAL 500 was Npgsql-only (Kind=Unspecified vs timestamptz) so InMemory cannot
/// reproduce it — live-verified separately (Release binary + real Postgres). These unit tests pin
/// the filter LOGIC on the superuser path (unfiltered) with Unspecified-Kind filters — exactly
/// the shape query-param binding produces.
/// </summary>
public class CheckoutHistoryReportTests
{
    private static Guid ActorId { get; } = Guid.NewGuid();

    /// <summary>Superuser consumes the unfiltered branch — pass-through stub for the handler DI.</summary>
    private sealed class PassThroughVisibility : IActionLogVisibilityService
    {
        public Task<List<ActionLog>> FilterVisibleLogsAsync(IReadOnlyList<ActionLog> logs, Guid userCompanyId)
            => Task.FromResult(logs.ToList());
    }

    private static CheckoutHistoryReportQueryHandler Ctx(AppDbContext db, bool super, Guid? companyId)
        => new(db, new TestHelpers.FakeScope { Super = super, CompanyId = companyId }, new PassThroughVisibility());

    [Fact]
    public async Task WithDateFilters_Superuser_ReturnsFilteredRows_NoThrow()
    {
        await using var db = TestHelpers.CreateContext(nameof(WithDateFilters_Superuser_ReturnsFilteredRows_NoThrow));
        var user = new User { Username = "hist-user", FirstName = "H", LastName = "U" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var now = DateTime.UtcNow;
        db.ActionLogs.AddRange(
            new ActionLog { ItemType = ItemType.Asset, ItemId = Guid.NewGuid(), ActionType = ActionType.Checkout, CreatedBy = user.Id, ActionDate = now.AddDays(-1) },
            new ActionLog { ItemType = ItemType.Asset, ItemId = Guid.NewGuid(), ActionType = ActionType.Checkout, CreatedBy = user.Id, ActionDate = now.AddDays(-40) });
        await db.SaveChangesAsync();

        // Kind=Unspecified (query-param binding shape) — the exact input that 500-ed on Npgsql.
        var result = await Ctx(db, true, null).Handle(
            new CheckoutHistoryReportQuery(now.AddDays(-7), now.AddDays(1)), CancellationToken.None);

        Assert.Single(result.Items); // only the log inside the 7-day window
    }

    [Fact]
    public async Task WithoutFilters_Superuser_ReturnsAll()
    {
        await using var db = TestHelpers.CreateContext(nameof(WithoutFilters_Superuser_ReturnsAll));
        var user = new User { Username = "hist-user2", FirstName = "H", LastName = "U" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.ActionLogs.Add(new ActionLog { ItemType = ItemType.Asset, ItemId = Guid.NewGuid(), ActionType = ActionType.Checkout, CreatedBy = user.Id, ActionDate = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var result = await Ctx(db, true, null).Handle(new CheckoutHistoryReportQuery(null, null), CancellationToken.None);

        Assert.Single(result.Items);
    }
}
