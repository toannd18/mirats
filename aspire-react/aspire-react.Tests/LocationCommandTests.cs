using aspire_react.Server.Application.Locations.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [BUG-G FIX] Company-scoping on Location.Create — SEC/HIGH behavior fix (Task L2 pattern,
/// identical to CreateDepartmentCommand). 4 cases per the approved design:
///   negative  — regular user of company A targeting company B → COMPANY_MISMATCH, no row, no log;
///   positive  — regular user targeting their OWN company → succeeds (UI flow not broken);
///   floater   — regular user leaving CompanyId null → succeeds (floater visible to everyone);
///   superuser — scope null → may target ANY company.
/// FakeScope pattern (user-approved: unit tests suffice for this 1-condition behavior fix — no
/// real Keycloak user needed). Handler-level (same level the Departments scoping is tested at).
/// </summary>
public class LocationCommandTests
{
    private static Guid ActorId { get; } = Guid.NewGuid();

    private static async Task<Company> SeedCompanyAsync(aspire_react.Server.Application.Common.Interfaces.IApplicationDbContext ctx, string name)
    {
        var c = new Company { Name = name };
        ctx.Companies.Add(c);
        await ctx.SaveChangesAsync();
        return c;
    }

    private static CreateLocationCommand Cmd(Guid? companyId) => new(
        Name: "PRT-BUGG loc",
        ParentId: null,
        CompanyId: companyId,
        ManagerId: null,
        Address: null,
        City: null,
        State: null,
        Country: null,
        Zip: null,
        CurrentUserId: ActorId);

    [Fact]
    public async Task RegularUser_TargetingOtherCompany_BlockedWithCompanyMismatch()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(RegularUser_TargetingOtherCompany_BlockedWithCompanyMismatch));
        var companyA = await SeedCompanyAsync(ctx, "CT-A");
        var companyB = await SeedCompanyAsync(ctx, "CT-B");
        var handler = new CreateLocationCommandHandler(ctx, new TestHelpers.FakeScope { Super = false, CompanyId = companyA.Id });

        var result = await handler.Handle(Cmd(companyB.Id), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("COMPANY_MISMATCH", result.ErrorCode);
        Assert.Equal(0, await ctx.Locations.CountAsync());                 // no row created
        Assert.Equal(0, await ctx.ActionLogs.CountAsync(l => l.ItemType == ItemType.Location)); // no log
    }

    [Fact]
    public async Task RegularUser_TargetingOwnCompany_Succeeds()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(RegularUser_TargetingOwnCompany_Succeeds));
        var companyA = await SeedCompanyAsync(ctx, "CT-A");
        var handler = new CreateLocationCommandHandler(ctx, new TestHelpers.FakeScope { Super = false, CompanyId = companyA.Id });

        var result = await handler.Handle(Cmd(companyA.Id), CancellationToken.None);

        Assert.True(result.Success);
        var loc = await ctx.Locations.SingleAsync();
        Assert.Equal(companyA.Id, loc.CompanyId);
        // ActionLog staged by ILoggableCommand path in production; handler-level assert the row only.
    }

    [Fact]
    public async Task RegularUser_LeavingCompanyNull_Floater_Succeeds()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(RegularUser_LeavingCompanyNull_Floater_Succeeds));
        await SeedCompanyAsync(ctx, "CT-A");
        var handler = new CreateLocationCommandHandler(ctx, new TestHelpers.FakeScope { Super = false, CompanyId = null });
        // NOTE: FakeScope with CompanyId = null models a company-less regular user; floater create allowed.

        var result = await handler.Handle(Cmd(null), CancellationToken.None);

        Assert.True(result.Success);
        var loc = await ctx.Locations.SingleAsync();
        Assert.Null(loc.CompanyId);
    }

    [Fact]
    public async Task Superuser_MayTargetAnyCompany()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Superuser_MayTargetAnyCompany));
        var companyA = await SeedCompanyAsync(ctx, "CT-A");
        var companyB = await SeedCompanyAsync(ctx, "CT-B");
        var handler = new CreateLocationCommandHandler(ctx, new TestHelpers.FakeScope { Super = true });

        var result = await handler.Handle(Cmd(companyB.Id), CancellationToken.None);

        Assert.True(result.Success);
        var loc = await ctx.Locations.SingleAsync();
        Assert.Equal(companyB.Id, loc.CompanyId);
    }
}
