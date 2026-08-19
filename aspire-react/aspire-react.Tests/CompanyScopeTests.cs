using System.Security.Claims;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// ST9/F41 — Company-scope security: the real CompanyScopeService.GetCurrentUserCompanyIdAsync
/// must resolve the acting user's CompanyId from the "local_user_id" claim, return null for
/// Superusers (realm_access admin), and fail closed (null) when the claim is missing or the
/// request is unauthenticated.
/// </summary>
public class CompanyScopeTests
{
    private static async Task<(AppDbContext ctx, Guid userId, Guid companyId)> SeedUserAsync(string dbName)
    {
        var ctx = TestHelpers.CreateContext(dbName);
        var company = new Company { Name = "CT-A" };
        ctx.Companies.Add(company);
        var user = new User { Username = "nv.a", Email = "a@t.local", FirstName = "A", LastName = "A", CompanyId = company.Id };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return (ctx, user.Id, company.Id);
    }

    private static DefaultHttpContext BuildHttpContext(ClaimsPrincipal principal, AppDbContext ctx)
    {
        var httpContext = new DefaultHttpContext { User = principal };
        var services = new ServiceCollection();
        services.AddSingleton(ctx); // resolve the SAME seeded AppDbContext from RequestServices
        httpContext.RequestServices = services.BuildServiceProvider();
        return httpContext;
    }

    private static ClaimsPrincipal RegularUser(Guid localUserId) =>
        new(new ClaimsIdentity(new[] { new Claim("local_user_id", localUserId.ToString()) }, "Test"));

    private static ClaimsPrincipal SuperUser() =>
        new(new ClaimsIdentity(new[]
        {
            new Claim("realm_access", """{"roles":["admin"]}""")
        }, "Test"));

    private static ClaimsPrincipal MissingClaim() =>
        new(new ClaimsIdentity(new[] { new Claim("preferred_username", "nv.a") }, "Test"));

    private static CompanyScopeService CreateService(DefaultHttpContext httpContext)
        => new(new HttpContextAccessor { HttpContext = httpContext }, new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task RegularUserWithLocalUserId_ReturnsTheirCompanyId()
    {
        var seed = await SeedUserAsync(nameof(RegularUserWithLocalUserId_ReturnsTheirCompanyId));
        await using var ctx = seed.ctx;
        var service = CreateService(BuildHttpContext(RegularUser(seed.userId), ctx));

        var result = await service.GetCurrentUserCompanyIdAsync();

        Assert.Equal(seed.companyId, result);
    }

    [Fact]
    public async Task SuperUserRealmRole_ReturnsNull()
    {
        var seed = await SeedUserAsync(nameof(SuperUserRealmRole_ReturnsNull));
        await using var ctx = seed.ctx;
        var service = CreateService(BuildHttpContext(SuperUser(), ctx));

        var result = await service.GetCurrentUserCompanyIdAsync();

        Assert.Null(result); // Superuser sees all companies
    }

    [Fact]
    public async Task MissingLocalUserIdClaim_ReturnsNull()
    {
        var seed = await SeedUserAsync(nameof(MissingLocalUserIdClaim_ReturnsNull));
        await using var ctx = seed.ctx;
        var service = CreateService(BuildHttpContext(MissingClaim(), ctx));

        var result = await service.GetCurrentUserCompanyIdAsync();

        Assert.Null(result); // fail closed — never guess from sub/preferred_username
    }

    [Fact]
    public async Task Unauthenticated_ReturnsNull()
    {
        var seed = await SeedUserAsync(nameof(Unauthenticated_ReturnsNull));
        await using var ctx = seed.ctx;
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal() }; // no authenticated identity
        var service = CreateService(httpContext);

        var result = await service.GetCurrentUserCompanyIdAsync();

        Assert.Null(result);
    }
}
