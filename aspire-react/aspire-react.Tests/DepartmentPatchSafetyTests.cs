using aspire_react.Server.Application.Departments.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [BUG-E FIX] Departments Update patch-safety — behavior change approved (full-PUT → Task M1/M2
/// patch semantics). Cases:
///   negative (bug reproduction): PUT sending ONLY {name} must NOT clear CompanyId/ManagerId/Phone/Fax
///     (previously they were wiped to null);
///   positive: a full payload still updates every field (old full-PUT callers keep working);
///   blank name when SENT → 400 (rule preserved); absent name → keep stored.
/// </summary>
public class DepartmentPatchSafetyTests
{
    private static Guid ActorId { get; } = Guid.NewGuid();

    private static async Task<Department> SeedDepartmentAsync(
        aspire_react.Server.Application.Common.Interfaces.IApplicationDbContext ctx, string name,
        Guid? companyId, string? phone, string? fax)
    {
        var d = new Department { Name = name, CompanyId = companyId, ManagerId = null, Phone = phone, Fax = fax };
        ctx.Departments.Add(d);
        await ctx.SaveChangesAsync();
        return d;
    }

    [Fact]
    public async Task Update_NameOnly_PreservesOtherFields_BugRepro()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Update_NameOnly_PreservesOtherFields_BugRepro));
        var company = new Company { Name = "CT-A" };
        ctx.Companies.Add(company);
        var d = await SeedDepartmentAsync(ctx, "IT", company.Id, "0123", "0456");
        var handler = new UpdateDepartmentCommandHandler(ctx, new TestHelpers.FakeScope { Super = true });

        // The BUG-E payload: ONLY the name — previously this cleared CompanyId/Phone/Fax.
        var result = await handler.Handle(
            new UpdateDepartmentCommand(d.Id, "IT v2", null, null, null, null, ActorId), CancellationToken.None);

        Assert.True(result.Success);
        var reloaded = await ctx.Departments.SingleAsync(x => x.Id == d.Id);
        Assert.Equal("IT v2", reloaded.Name);
        Assert.Equal(company.Id, reloaded.CompanyId);   // was cleared before the fix
        Assert.Equal("0123", reloaded.Phone);           // was cleared before the fix
        Assert.Equal("0456", reloaded.Fax);             // was cleared before the fix
    }

    [Fact]
    public async Task Update_FullPayload_StillUpdatesEveryField_Positive()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Update_FullPayload_StillUpdatesEveryField_Positive));
        var companyA = new Company { Name = "CT-A" };
        var companyB = new Company { Name = "CT-B" };
        ctx.Companies.AddRange(companyA, companyB);
        var d = await SeedDepartmentAsync(ctx, "IT", companyA.Id, "0123", "0456");
        var handler = new UpdateDepartmentCommandHandler(ctx, new TestHelpers.FakeScope { Super = true });

        var result = await handler.Handle(
            new UpdateDepartmentCommand(d.Id, "Ops", companyB.Id, null, "9999", "8888", ActorId), CancellationToken.None);

        Assert.True(result.Success);
        var reloaded = await ctx.Departments.SingleAsync(x => x.Id == d.Id);
        Assert.Equal("Ops", reloaded.Name);
        Assert.Equal(companyB.Id, reloaded.CompanyId);
        Assert.Equal("9999", reloaded.Phone);
        Assert.Equal("8888", reloaded.Fax);
    }

    [Fact]
    public async Task Update_BlankNameSent_Rejected()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Update_BlankNameSent_Rejected));
        var d = await SeedDepartmentAsync(ctx, "IT", null, null, null);
        var handler = new UpdateDepartmentCommandHandler(ctx, new TestHelpers.FakeScope { Super = true });

        var result = await handler.Handle(
            new UpdateDepartmentCommand(d.Id, "   ", null, null, null, null, ActorId), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Tên phòng ban không được để trống.", result.Message);
    }

    [Fact]
    public async Task Update_DuplicateName_Rejected()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(Update_DuplicateName_Rejected));
        var d1 = await SeedDepartmentAsync(ctx, "IT", null, null, null);
        await SeedDepartmentAsync(ctx, "HR", null, null, null);
        var handler = new UpdateDepartmentCommandHandler(ctx, new TestHelpers.FakeScope { Super = true });

        var result = await handler.Handle(
            new UpdateDepartmentCommand(d1.Id, "HR", null, null, null, null, ActorId), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Tên phòng ban đã tồn tại.", result.Message);
    }
}
