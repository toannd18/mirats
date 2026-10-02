using aspire_react.Server.Application.Users.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Infrastructure.Authentication;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// FIX-N5 (phần còn lại, audit 2026-10-02) — DepartmentId/LocationId scope validation.
///
/// CompanyId was already scope-checked, but the two other company-owned references on a User were
/// not: verified live before the fix, an admin of company A could attach a user to a department or
/// location of company B (cross-tenant reference — same class as BUG-G on a different field).
/// Rules under test: reference must exist (RESOURCE_NOT_FOUND, was a raw FK 500) and, when it
/// points at a real company, that company must be inside the actor's scope (COMPANY_MISMATCH);
/// floaters (CompanyId == null) are always allowed and superusers are unrestricted.
/// </summary>
public class UserReferenceScopeTests
{
    private static readonly Guid ActorId = Guid.NewGuid();

    private static AppDbContext CreateContext(string name) => TestHelpers.CreateContext(name);

    private static UpdateUserCommandHandler UpdateHandler(AppDbContext ctx, TestHelpers.FakeScope scope)
        => new(ctx, TestHelpers.CreateActionLogService(ctx, ActorId), scope, NullLogger<UpdateUserCommandHandler>.Instance);

    private static CreateUserCommandHandler CreateHandler(AppDbContext ctx, TestHelpers.FakeScope scope)
        => new(ctx, new PasswordHasherService(), TestHelpers.CreateActionLogService(ctx, ActorId), scope, NullLogger<CreateUserCommandHandler>.Instance);

    private sealed record Fixture(Guid CompanyA, Guid CompanyB, Guid DeptA, Guid DeptB, Guid DeptFloater, Guid LocA, Guid LocB);

    private static async Task<Fixture> SeedAsync(AppDbContext ctx)
    {
        var a = new Company { Name = "CT-A" };
        var b = new Company { Name = "CT-B" };
        ctx.Companies.AddRange(a, b);
        var deptA = new Department { Name = "Dept-A", CompanyId = a.Id };
        var deptB = new Department { Name = "Dept-B", CompanyId = b.Id };
        var deptF = new Department { Name = "Dept-Floater", CompanyId = null };
        var locA = new Location { Name = "Loc-A", CompanyId = a.Id };
        var locB = new Location { Name = "Loc-B", CompanyId = b.Id };
        ctx.Departments.AddRange(deptA, deptB, deptF);
        ctx.Locations.AddRange(locA, locB);
        await ctx.SaveChangesAsync();
        return new Fixture(a.Id, b.Id, deptA.Id, deptB.Id, deptF.Id, locA.Id, locB.Id);
    }

    private static User NewUser(Guid? companyId)
        => new() { Username = "u1", Email = "u1@example.test", FirstName = "F", LastName = "L", CompanyId = companyId };

    // =========================================================================
    // UPDATE
    // =========================================================================

    [Fact]
    public async Task Update_DepartmentOfOtherCompany_Rejected_NotMutated()
    {
        await using var ctx = CreateContext(nameof(Update_DepartmentOfOtherCompany_Rejected_NotMutated));
        var f = await SeedAsync(ctx);
        var user = NewUser(f.CompanyA);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var handler = UpdateHandler(ctx, new TestHelpers.FakeScope { CompanyId = f.CompanyA });
        var result = await handler.Handle(new UpdateUserCommand
        {
            Id = user.Id,
            FirstName = "F",
            LastName = "L",
            Email = "u1@example.test",
            DepartmentId = f.DeptB
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("COMPANY_MISMATCH", result.ErrorCode);
        var reloaded = await ctx.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Null(reloaded.DepartmentId);
    }

    [Fact]
    public async Task Update_LocationOfOtherCompany_Rejected_NotMutated()
    {
        await using var ctx = CreateContext(nameof(Update_LocationOfOtherCompany_Rejected_NotMutated));
        var f = await SeedAsync(ctx);
        var user = NewUser(f.CompanyA);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var handler = UpdateHandler(ctx, new TestHelpers.FakeScope { CompanyId = f.CompanyA });
        var result = await handler.Handle(new UpdateUserCommand
        {
            Id = user.Id,
            FirstName = "F",
            LastName = "L",
            Email = "u1@example.test",
            LocationId = f.LocB
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("COMPANY_MISMATCH", result.ErrorCode);
        var reloaded = await ctx.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Null(reloaded.LocationId);
    }

    [Fact]
    public async Task Update_OwnCompanyDepartmentAndLocation_Allowed()
    {
        await using var ctx = CreateContext(nameof(Update_OwnCompanyDepartmentAndLocation_Allowed));
        var f = await SeedAsync(ctx);
        var user = NewUser(f.CompanyA);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var handler = UpdateHandler(ctx, new TestHelpers.FakeScope { CompanyId = f.CompanyA });
        var result = await handler.Handle(new UpdateUserCommand
        {
            Id = user.Id,
            FirstName = "F",
            LastName = "L",
            Email = "u1@example.test",
            DepartmentId = f.DeptA,
            LocationId = f.LocA
        }, CancellationToken.None);

        Assert.True(result.Success);
        var reloaded = await ctx.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(f.DeptA, reloaded.DepartmentId);
        Assert.Equal(f.LocA, reloaded.LocationId);
    }

    [Fact]
    public async Task Update_FloaterDepartment_Allowed()
    {
        await using var ctx = CreateContext(nameof(Update_FloaterDepartment_Allowed));
        var f = await SeedAsync(ctx);
        var user = NewUser(f.CompanyA);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var handler = UpdateHandler(ctx, new TestHelpers.FakeScope { CompanyId = f.CompanyA });
        var result = await handler.Handle(new UpdateUserCommand
        {
            Id = user.Id,
            FirstName = "F",
            LastName = "L",
            Email = "u1@example.test",
            DepartmentId = f.DeptFloater
        }, CancellationToken.None);

        Assert.True(result.Success); // company-less reference: "own company or floater" rule
        Assert.Equal(f.DeptFloater, (await ctx.Users.SingleAsync(u => u.Id == user.Id)).DepartmentId);
    }

    [Fact]
    public async Task Update_SuperUser_AnyDepartment_Allowed()
    {
        await using var ctx = CreateContext(nameof(Update_SuperUser_AnyDepartment_Allowed));
        var f = await SeedAsync(ctx);
        var user = NewUser(f.CompanyA);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var handler = UpdateHandler(ctx, new TestHelpers.FakeScope { Super = true });
        var result = await handler.Handle(new UpdateUserCommand
        {
            Id = user.Id,
            FirstName = "F",
            LastName = "L",
            Email = "u1@example.test",
            DepartmentId = f.DeptB,
            LocationId = f.LocB
        }, CancellationToken.None);

        Assert.True(result.Success);
        var reloaded = await ctx.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(f.DeptB, reloaded.DepartmentId);
        Assert.Equal(f.LocB, reloaded.LocationId);
    }

    [Fact]
    public async Task Update_NonExistentDepartment_ResourceNotFound()
    {
        await using var ctx = CreateContext(nameof(Update_NonExistentDepartment_ResourceNotFound));
        var f = await SeedAsync(ctx);
        var user = NewUser(f.CompanyA);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var handler = UpdateHandler(ctx, new TestHelpers.FakeScope { Super = true });
        var result = await handler.Handle(new UpdateUserCommand
        {
            Id = user.Id,
            FirstName = "F",
            LastName = "L",
            Email = "u1@example.test",
            DepartmentId = Guid.NewGuid() // BUG-H class: was a raw FK 500
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("RESOURCE_NOT_FOUND", result.ErrorCode);
    }

    // =========================================================================
    // CREATE
    // =========================================================================

    [Fact]
    public async Task Create_DepartmentOfOtherCompany_Rejected_NoRowCreated()
    {
        await using var ctx = CreateContext(nameof(Create_DepartmentOfOtherCompany_Rejected_NoRowCreated));
        var f = await SeedAsync(ctx);

        var handler = CreateHandler(ctx, new TestHelpers.FakeScope { CompanyId = f.CompanyA });
        var result = await handler.Handle(new CreateUserCommand
        {
            Username = "nv.x",
            Email = "nv.x@example.test",
            FirstName = "N",
            LastName = "X",
            Password = "Init#Pass2026",
            CompanyId = f.CompanyA,
            DepartmentId = f.DeptB
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("COMPANY_MISMATCH", result.ErrorCode);
        Assert.False(await ctx.Users.AnyAsync(u => u.Username == "nv.x"));
    }

    [Fact]
    public async Task Create_OwnCompanyDepartment_Allowed()
    {
        await using var ctx = CreateContext(nameof(Create_OwnCompanyDepartment_Allowed));
        var f = await SeedAsync(ctx);

        var handler = CreateHandler(ctx, new TestHelpers.FakeScope { CompanyId = f.CompanyA });
        var result = await handler.Handle(new CreateUserCommand
        {
            Username = "nv.y",
            Email = "nv.y@example.test",
            FirstName = "N",
            LastName = "Y",
            Password = "Init#Pass2026",
            CompanyId = f.CompanyA,
            DepartmentId = f.DeptA,
            LocationId = f.LocA
        }, CancellationToken.None);

        Assert.True(result.Success);
        var created = await ctx.Users.SingleAsync(u => u.Username == "nv.y");
        Assert.Equal(f.DeptA, created.DepartmentId);
        Assert.Equal(f.LocA, created.LocationId);
    }

    [Fact]
    public async Task Create_NonExistentLocation_ResourceNotFound()
    {
        await using var ctx = CreateContext(nameof(Create_NonExistentLocation_ResourceNotFound));
        var f = await SeedAsync(ctx);

        var handler = CreateHandler(ctx, new TestHelpers.FakeScope { Super = true });
        var result = await handler.Handle(new CreateUserCommand
        {
            Username = "nv.z",
            Email = "nv.z@example.test",
            FirstName = "N",
            LastName = "Z",
            Password = "Init#Pass2026",
            CompanyId = f.CompanyA,
            LocationId = Guid.NewGuid()
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("RESOURCE_NOT_FOUND", result.ErrorCode);
        Assert.False(await ctx.Users.AnyAsync(u => u.Username == "nv.z"));
    }
}
