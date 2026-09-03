using System.Security.Claims;
using aspire_react.Server.Application.SystemInfos.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// Task IMPORT-T7 (follow-up) — company-scoping on SystemInfo Create + AddPosition,
/// the same Task L2 class of fix applied to SystemInfo/SystemPosition. A regular user may only
/// create a system for their own company (or company-less floater); AddPosition only for a system
/// in their own company scope. Superuser bypasses. Mirror the exact convention used at Task L2:
/// Create out-of-scope → 400 COMPANY_MISMATCH; AddPosition out-of-scope → 404 (hide existence).
/// [Giai đoạn 3] SystemInfo migrated to MediatR — tests drive the command handlers directly with
/// the REAL CompanyScopeService (claims-based resolution preserved via the seeded HttpContext —
/// same scope substance as the old controller-level tests).
/// </summary>
public class SystemInfoCreateCompanyScopeTests
{
    private static async Task<(AppDbContext ctx, Guid companyA, Guid companyB, Guid regularUserA, Guid superUserId)> SeedAsync(string dbName)
    {
        var ctx = TestHelpers.CreateContext(dbName);
        var coA = new Company { Name = "CT-A" };
        var coB = new Company { Name = "CT-B" };
        ctx.Companies.Add(coA);
        ctx.Companies.Add(coB);
        await ctx.SaveChangesAsync();

        var ua = new User { Username = "ua", Email = "ua@t.local", FirstName = "U", LastName = "A", CompanyId = coA.Id };
        ctx.Users.Add(ua);
        await ctx.SaveChangesAsync();
        return (ctx, coA.Id, coB.Id, ua.Id, Guid.NewGuid());
    }

    private static DefaultHttpContext BuildHttpContext(ClaimsPrincipal principal, AppDbContext ctx)
    {
        var httpContext = new DefaultHttpContext { User = principal };
        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        httpContext.RequestServices = services.BuildServiceProvider();
        return httpContext;
    }

    private static ClaimsPrincipal RegularUser(Guid localUserId) =>
        new(new ClaimsIdentity(new[] { new Claim("local_user_id", localUserId.ToString()) }, "Test"));

    private static ClaimsPrincipal SuperUser() =>
        new(new ClaimsIdentity(new[] { new Claim("realm_access", """{"roles":["admin"]}""") }, "Test"));

    private static (CreateSystemInfoCommandHandler Create, AddSystemPositionCommandHandler AddPos) BuildHandlers(AppDbContext ctx, ClaimsPrincipal principal)
    {
        var httpContext = BuildHttpContext(principal, ctx);
        var scope = new CompanyScopeService(new HttpContextAccessor { HttpContext = httpContext }, new MemoryCache(new MemoryCacheOptions()));
        return (new CreateSystemInfoCommandHandler(ctx, scope), new AddSystemPositionCommandHandler(ctx, scope));
    }

    // ── Create ──

    [Fact]
    public async Task RegularUser_CreateForOtherCompany_Returns400CompanyMismatch()
    {
        var s = await SeedAsync(nameof(RegularUser_CreateForOtherCompany_Returns400CompanyMismatch));
        await using var ctx = s.ctx;
        var handlers = BuildHandlers(ctx, RegularUser(s.regularUserA));

        var result = await handlers.Create.Handle(
            new CreateSystemInfoCommand($"SYS-{DateTime.Now.Year}-001", "HT-B", null, s.companyB, s.regularUserA), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("COMPANY_MISMATCH", result.ErrorCode);
        Assert.False(await ctx.SystemInfos.AnyAsync(x => x.Name == "HT-B")); // not created
    }

    [Fact]
    public async Task RegularUser_CreateForOwnCompany_Returns200()
    {
        var s = await SeedAsync(nameof(RegularUser_CreateForOwnCompany_Returns200));
        await using var ctx = s.ctx;
        var handlers = BuildHandlers(ctx, RegularUser(s.regularUserA));

        var result = await handlers.Create.Handle(
            new CreateSystemInfoCommand($"SYS-{DateTime.Now.Year}-001", "HT-A", null, s.companyA, s.regularUserA), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(await ctx.SystemInfos.AnyAsync(x => x.Name == "HT-A" && x.CompanyId == s.companyA));
    }

    [Fact]
    public async Task RegularUser_CreateFloater_Returns200()
    {
        var s = await SeedAsync(nameof(RegularUser_CreateFloater_Returns200));
        await using var ctx = s.ctx;
        var handlers = BuildHandlers(ctx, RegularUser(s.regularUserA));

        var result = await handlers.Create.Handle(
            new CreateSystemInfoCommand($"SYS-{DateTime.Now.Year}-001", "HT-Floater", null, null, s.regularUserA), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(await ctx.SystemInfos.AnyAsync(x => x.Name == "HT-Floater" && x.CompanyId == null));
    }

    [Fact]
    public async Task SuperUser_CreateForAnyCompany_Returns200()
    {
        var s = await SeedAsync(nameof(SuperUser_CreateForAnyCompany_Returns200));
        await using var ctx = s.ctx;
        var handlers = BuildHandlers(ctx, SuperUser());

        var result = await handlers.Create.Handle(
            new CreateSystemInfoCommand($"SYS-{DateTime.Now.Year}-001", "HT-B-Super", null, s.companyB, s.superUserId), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(await ctx.SystemInfos.AnyAsync(x => x.Name == "HT-B-Super" && x.CompanyId == s.companyB));
    }

    // ── AddPosition ──

    private static async Task<Guid> SeedSystemAsync(AppDbContext ctx, string code, string name, Guid? companyId)
    {
        var sys = new SystemInfo { Code = code, Name = name, CompanyId = companyId };
        ctx.SystemInfos.Add(sys);
        await ctx.SaveChangesAsync();
        return sys.Id;
    }

    [Fact]
    public async Task RegularUser_AddPositionToOtherCompanySystem_Returns404()
    {
        var s = await SeedAsync(nameof(RegularUser_AddPositionToOtherCompanySystem_Returns404));
        await using var ctx = s.ctx;
        var sysId = await SeedSystemAsync(ctx, $"SYS-{DateTime.Now.Year}-001", "HT-B", s.companyB);
        var handlers = BuildHandlers(ctx, RegularUser(s.regularUserA));

        var result = await handlers.AddPos.Handle(
            new AddSystemPositionCommand(sysId, $"POS-{DateTime.Now.Year}-001", "VT-1", null, s.regularUserA), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("NOT_FOUND", result.ErrorCode);
        Assert.False(await ctx.SystemPositions.AnyAsync(p => p.Name == "VT-1")); // not created
    }

    [Fact]
    public async Task RegularUser_AddPositionToOwnCompanySystem_Returns200()
    {
        var s = await SeedAsync(nameof(RegularUser_AddPositionToOwnCompanySystem_Returns200));
        await using var ctx = s.ctx;
        var sysId = await SeedSystemAsync(ctx, $"SYS-{DateTime.Now.Year}-001", "HT-A", s.companyA);
        var handlers = BuildHandlers(ctx, RegularUser(s.regularUserA));

        var result = await handlers.AddPos.Handle(
            new AddSystemPositionCommand(sysId, $"POS-{DateTime.Now.Year}-001", "VT-A", null, s.regularUserA), CancellationToken.None);

        Assert.True(result.Success);
        var pos = await ctx.SystemPositions.SingleAsync();
        Assert.Equal(sysId, pos.SystemInfoId);
        Assert.Equal(s.companyA, pos.SystemInfo!.CompanyId); // inherits parent company
    }

    [Fact]
    public async Task RegularUser_AddPositionToCompanyLessSystem_Returns200()
    {
        var s = await SeedAsync(nameof(RegularUser_AddPositionToCompanyLessSystem_Returns200));
        await using var ctx = s.ctx;
        var sysId = await SeedSystemAsync(ctx, $"SYS-{DateTime.Now.Year}-001", "HT-Floater", null);
        var handlers = BuildHandlers(ctx, RegularUser(s.regularUserA));

        var result = await handlers.AddPos.Handle(
            new AddSystemPositionCommand(sysId, $"POS-{DateTime.Now.Year}-001", "VT-Floater", null, s.regularUserA), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Single(await ctx.SystemPositions.ToListAsync());
    }

    [Fact]
    public async Task SuperUser_AddPositionToAnySystem_Returns200()
    {
        var s = await SeedAsync(nameof(SuperUser_AddPositionToAnySystem_Returns200));
        await using var ctx = s.ctx;
        var sysId = await SeedSystemAsync(ctx, $"SYS-{DateTime.Now.Year}-001", "HT-B", s.companyB);
        var handlers = BuildHandlers(ctx, SuperUser());

        var result = await handlers.AddPos.Handle(
            new AddSystemPositionCommand(sysId, $"POS-{DateTime.Now.Year}-001", "VT-B", null, s.superUserId), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Single(await ctx.SystemPositions.ToListAsync());
    }
}
