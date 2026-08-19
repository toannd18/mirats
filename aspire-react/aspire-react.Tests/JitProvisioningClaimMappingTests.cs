using System.Security.Claims;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// Task BACKLOG-2 (Mục 1) — JIT provisioning must read email/given_name/family_name even when
/// ASP.NET's default MapInboundClaims renames them to long URIs (ClaimTypes.Email/GivenName/Surname).
/// </summary>
public class JitProvisioningClaimMappingTests
{
    private static AppDbContext CreateContext(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options, new TestHelpers.SuperUserScope());
    }

    private static ClaimsPrincipal Principal(params Claim[] claims)
        => new(new ClaimsIdentity(claims, "Test"));

    [Fact]
    public async Task Reads_Email_GivenName_FamilyName_Via_Short_Oidc_Names()
    {
        var db = CreateContext(nameof(Reads_Email_GivenName_FamilyName_Via_Short_Oidc_Names));
        var svc = new JitUserProvisioningService(db);
        var principal = Principal(
            new Claim("preferred_username", "john.doe"),
            new Claim("email", "john@example.com"),
            new Claim("given_name", "John"),
            new Claim("family_name", "Doe"));

        var id = await svc.ProvisionAsync(principal);

        Assert.NotNull(id);
        var user = await db.Users.SingleAsync(u => u.Id == id);
        Assert.Equal("john@example.com", user.Email);
        Assert.Equal("John", user.FirstName);
        Assert.Equal("Doe", user.LastName);
    }

    [Fact]
    public async Task Reads_Email_GivenName_FamilyName_Via_Mapped_ClaimTypes_Names()
    {
        var db = CreateContext(nameof(Reads_Email_GivenName_FamilyName_Via_Mapped_ClaimTypes_Names));
        var svc = new JitUserProvisioningService(db);
        // Simulate MapInboundClaims=true (ASP.NET default): short OIDC names are absent, long URIs present.
        var principal = Principal(
            new Claim("preferred_username", "jane.doe"),
            new Claim(ClaimTypes.Email, "jane@example.com"),
            new Claim(ClaimTypes.GivenName, "Jane"),
            new Claim(ClaimTypes.Surname, "Doe"));

        var id = await svc.ProvisionAsync(principal);

        Assert.NotNull(id);
        var user = await db.Users.SingleAsync(u => u.Id == id);
        Assert.Equal("jane@example.com", user.Email);
        Assert.Equal("Jane", user.FirstName);
        Assert.Equal("Doe", user.LastName);
    }

    [Fact]
    public async Task Falls_Back_To_Placeholder_When_No_Email_Claim()
    {
        var db = CreateContext(nameof(Falls_Back_To_Placeholder_When_No_Email_Claim));
        var svc = new JitUserProvisioningService(db);
        var principal = Principal(new Claim("preferred_username", "nobody"));

        var id = await svc.ProvisionAsync(principal);

        Assert.NotNull(id);
        var user = await db.Users.SingleAsync(u => u.Id == id);
        Assert.Equal("nobody@placeholder.local", user.Email);
    }
}
