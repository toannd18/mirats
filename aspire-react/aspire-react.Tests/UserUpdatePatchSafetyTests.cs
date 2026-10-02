using aspire_react.Server.Application.Users.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// FIX-N1 + FIX-N5 (audit 2026-10-02) — UpdateUserCommand:
///
/// N1 (patch-safety regression, introduced by the AUTH Phase 4 local-only rewrite): a partial
/// payload used to silently WIPE CompanyId / DepartmentId / LocationId / EmployeeNumber / JobTitle
/// (the same "wiped real data" bug class as BUG-E / BUG-N). Now: absent → keep.
/// Three-valued convention for the nullable Guid fields, reusing the project-wide
/// "Guid.Empty = floater/none" sentinel (CompanyScopeService, AssetMaintenance.CompanyId,
/// ImportCommands): absent → keep, Guid.Empty → clear to null, real Guid → set.
///
/// N5 (company-scoping gap): the NEW CompanyId was not validated against the actor's scope, so a
/// regular admin of company A could move a user into company B. Now: Task L2 / CreateUser pattern —
/// regular user may only assign their own company (or clear to floater); superuser unrestricted;
/// checked BEFORE any mutation (rejected request leaves the row untouched).
/// </summary>
public class UserUpdatePatchSafetyTests
{
    private static AppDbContext CreateContext(string name)
        => TestHelpers.CreateContext(name);

    private static UpdateUserCommandHandler BuildHandler(AppDbContext ctx, TestHelpers.FakeScope scope, Guid actorId)
        => new(ctx, scope, new aspire_react.Server.Infrastructure.Authorization.PermissionLockoutGuard(ctx),
            NullLogger<UpdateUserCommandHandler>.Instance);

    private static readonly Guid ActorId = Guid.NewGuid();

    private static async Task<(Guid ctA, Guid ctB, Guid deptA, Guid locA)> SeedAsync(AppDbContext ctx)
    {
        var ctA = new Company { Name = "CT-A" };
        var ctB = new Company { Name = "CT-B" };
        var dept = new Department { Name = "Dept-A", CompanyId = ctA.Id };
        var loc = new Location { Name = "Loc-A", CompanyId = ctA.Id };
        ctx.Companies.AddRange(ctA, ctB);
        ctx.Departments.Add(dept);
        ctx.Locations.Add(loc);
        await ctx.SaveChangesAsync();
        return (ctA.Id, ctB.Id, dept.Id, loc.Id);
    }

    private static User NewUser(Guid? companyId = null, Guid? departmentId = null, Guid? locationId = null)
        => new()
        {
            Username = "u1",
            Email = "u1@example.test",
            FirstName = "First",
            LastName = "Last",
            CompanyId = companyId,
            DepartmentId = departmentId,
            LocationId = locationId,
            EmployeeNumber = "EMP-1",
            JobTitle = "Original Title"
        };

    // =========================================================================
    // N1 — bug-repro: partial payload must NOT wipe the five fields
    // =========================================================================

    [Fact]
    public async Task Update_PartialPayload_PreservesCompanyDepartmentLocationEmployeeNumberJobTitle()
    {
        await using var ctx = CreateContext(nameof(Update_PartialPayload_PreservesCompanyDepartmentLocationEmployeeNumberJobTitle));
        var (ctA, _, deptA, locA) = await SeedAsync(ctx);
        var user = NewUser(ctA, deptA, locA);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var handler = BuildHandler(ctx, new TestHelpers.FakeScope { Super = true }, ActorId);

        // The exact T4 payload from the audit: id + names + email, nothing else.
        var result = await handler.Handle(new UpdateUserCommand
        {
            Id = user.Id,
            FirstName = "NewFirst",
            LastName = "NewLast",
            Email = "u1@example.test"
        }, CancellationToken.None);

        Assert.True(result.Success);
        var reloaded = await ctx.Users.SingleAsync(x => x.Id == user.Id);
        Assert.Equal(ctA, reloaded.CompanyId);              // was wiped to null before FIX-N1
        Assert.Equal(deptA, reloaded.DepartmentId);         // was wiped to null before FIX-N1
        Assert.Equal(locA, reloaded.LocationId);            // was wiped to null before FIX-N1
        Assert.Equal("EMP-1", reloaded.EmployeeNumber);     // was wiped to null before FIX-N1
        Assert.Equal("Original Title", reloaded.JobTitle);  // was wiped to null before FIX-N1
        Assert.Equal("NewFirst", reloaded.FirstName);       // sent fields still applied
    }

    [Fact]
    public async Task Update_CompanyId_EmptyGuidSentinel_ClearsToFloater()
    {
        await using var ctx = CreateContext(nameof(Update_CompanyId_EmptyGuidSentinel_ClearsToFloater));
        var (ctA, _, _, _) = await SeedAsync(ctx);
        var user = NewUser(ctA);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var handler = BuildHandler(ctx, new TestHelpers.FakeScope { Super = true }, ActorId);

        var result = await handler.Handle(new UpdateUserCommand
        {
            Id = user.Id,
            FirstName = "First",
            LastName = "Last",
            Email = "u1@example.test",
            CompanyId = Guid.Empty // explicit "clear to floater" sentinel
        }, CancellationToken.None);

        Assert.True(result.Success);
        var reloaded = await ctx.Users.SingleAsync(x => x.Id == user.Id);
        Assert.Null(reloaded.CompanyId);
    }

    [Fact]
    public async Task Update_DepartmentAndLocation_EmptyGuidSentinel_Clears()
    {
        await using var ctx = CreateContext(nameof(Update_DepartmentAndLocation_EmptyGuidSentinel_Clears));
        var (ctA, _, deptA, locA) = await SeedAsync(ctx);
        var user = NewUser(ctA, deptA, locA);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var handler = BuildHandler(ctx, new TestHelpers.FakeScope { Super = true }, ActorId);

        var result = await handler.Handle(new UpdateUserCommand
        {
            Id = user.Id,
            FirstName = "First",
            LastName = "Last",
            Email = "u1@example.test",
            DepartmentId = Guid.Empty,
            LocationId = Guid.Empty
        }, CancellationToken.None);

        Assert.True(result.Success);
        var reloaded = await ctx.Users.SingleAsync(x => x.Id == user.Id);
        Assert.Null(reloaded.DepartmentId);
        Assert.Null(reloaded.LocationId);
        Assert.Equal(ctA, reloaded.CompanyId); // untouched field keeps its value
    }

    [Fact]
    public async Task Update_EmployeeNumberEmptyString_ClearsTextButAbsentKeeps()
    {
        await using var ctx = CreateContext(nameof(Update_EmployeeNumberEmptyString_ClearsTextButAbsentKeeps));
        var (ctA, _, _, _) = await SeedAsync(ctx);
        var user = NewUser(ctA);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var handler = BuildHandler(ctx, new TestHelpers.FakeScope { Super = true }, ActorId);

        var result = await handler.Handle(new UpdateUserCommand
        {
            Id = user.Id,
            FirstName = "First",
            LastName = "Last",
            Email = "u1@example.test",
            EmployeeNumber = "",   // sent empty → cleared (BUG-N convention)
            JobTitle = null        // absent → kept
        }, CancellationToken.None);

        Assert.True(result.Success);
        var reloaded = await ctx.Users.SingleAsync(x => x.Id == user.Id);
        Assert.Equal(string.Empty, reloaded.EmployeeNumber);
        Assert.Equal("Original Title", reloaded.JobTitle);
    }

    // =========================================================================
    // N5 — company-scoping on the NEW CompanyId (Task L2 / CreateUser pattern)
    // =========================================================================

    [Fact]
    public async Task Update_CrossCompany_RegularUser_Rejected_NotMutated()
    {
        await using var ctx = CreateContext(nameof(Update_CrossCompany_RegularUser_Rejected_NotMutated));
        var (ctA, ctB, _, _) = await SeedAsync(ctx);
        var user = NewUser(ctA);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        // Actor is a REGULAR user whose scope is company A → moving the user to company B is denied.
        var handler = BuildHandler(ctx, new TestHelpers.FakeScope { CompanyId = ctA }, ActorId);

        var result = await handler.Handle(new UpdateUserCommand
        {
            Id = user.Id,
            FirstName = "Hacked",
            LastName = "Name",
            Email = "changed@example.test",
            CompanyId = ctB
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("COMPANY_MISMATCH", result.ErrorCode);
        var reloaded = await ctx.Users.SingleAsync(x => x.Id == user.Id);
        Assert.Equal(ctA, reloaded.CompanyId);        // rejected BEFORE mutation
        Assert.Equal("First", reloaded.FirstName);    // no partial write either
        Assert.Equal("u1@example.test", reloaded.Email);
    }

    [Fact]
    public async Task Update_SameCompany_RegularUser_Allowed()
    {
        await using var ctx = CreateContext(nameof(Update_SameCompany_RegularUser_Allowed));
        var (ctA, _, _, _) = await SeedAsync(ctx);
        var user = NewUser(null); // currently a floater
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var handler = BuildHandler(ctx, new TestHelpers.FakeScope { CompanyId = ctA }, ActorId);

        var result = await handler.Handle(new UpdateUserCommand
        {
            Id = user.Id,
            FirstName = "First",
            LastName = "Last",
            Email = "u1@example.test",
            CompanyId = ctA // own company → allowed
        }, CancellationToken.None);

        Assert.True(result.Success);
        var reloaded = await ctx.Users.SingleAsync(x => x.Id == user.Id);
        Assert.Equal(ctA, reloaded.CompanyId);
    }

    [Fact]
    public async Task Update_FloaterSentinel_RegularUser_Allowed()
    {
        await using var ctx = CreateContext(nameof(Update_FloaterSentinel_RegularUser_Allowed));
        var (ctA, _, _, _) = await SeedAsync(ctx);
        var user = NewUser(ctA);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var handler = BuildHandler(ctx, new TestHelpers.FakeScope { CompanyId = ctA }, ActorId);

        // Clearing to floater mirrors CreateUser's "own company OR floater" rule → allowed.
        var result = await handler.Handle(new UpdateUserCommand
        {
            Id = user.Id,
            FirstName = "First",
            LastName = "Last",
            Email = "u1@example.test",
            CompanyId = Guid.Empty
        }, CancellationToken.None);

        Assert.True(result.Success);
        var reloaded = await ctx.Users.SingleAsync(x => x.Id == user.Id);
        Assert.Null(reloaded.CompanyId);
    }

    [Fact]
    public async Task Update_AnyCompany_SuperUser_Allowed()
    {
        await using var ctx = CreateContext(nameof(Update_AnyCompany_SuperUser_Allowed));
        var (ctA, ctB, _, _) = await SeedAsync(ctx);
        var user = NewUser(ctA);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var handler = BuildHandler(ctx, new TestHelpers.FakeScope { Super = true }, ActorId);

        var result = await handler.Handle(new UpdateUserCommand
        {
            Id = user.Id,
            FirstName = "First",
            LastName = "Last",
            Email = "u1@example.test",
            CompanyId = ctB
        }, CancellationToken.None);

        Assert.True(result.Success);
        var reloaded = await ctx.Users.SingleAsync(x => x.Id == user.Id);
        Assert.Equal(ctB, reloaded.CompanyId);
    }

    [Fact]
    public async Task Update_CompanyLessRegularUser_AssigningRealCompany_Rejected()
    {
        await using var ctx = CreateContext(nameof(Update_CompanyLessRegularUser_AssigningRealCompany_Rejected));
        var (ctA, _, _, _) = await SeedAsync(ctx);
        var user = NewUser(null);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        // Scope GUID.Empty sentinel = company-less regular user → may not target a real company
        // (same rule CreateUser enforces, per SEC-FIX JIT-COMPANYLESS).
        var handler = BuildHandler(ctx, new TestHelpers.FakeScope { CompanyId = null }, ActorId);

        var result = await handler.Handle(new UpdateUserCommand
        {
            Id = user.Id,
            FirstName = "First",
            LastName = "Last",
            Email = "u1@example.test",
            CompanyId = ctA
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("COMPANY_MISMATCH", result.ErrorCode);
        var reloaded = await ctx.Users.SingleAsync(x => x.Id == user.Id);
        Assert.Null(reloaded.CompanyId);
    }
}
