using System.Text.Json;
using aspire_react.Server.Application.Users.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// ST9/F41 — User CRUD ActionLog coverage (ST5): CreateUserCommand / UpdateUserCommand /
/// DeleteUserCommand must write the matching ActionLog row (with CompanyId and the
/// { changes: { field: { old, new } } } meta for updates), with IKeycloakService mocked.
/// </summary>
public class UserActionLogTests
{
    private static Guid ActorId { get; } = Guid.NewGuid();

    private static async Task<Guid> SeedCompanyAsync(AppDbContext ctx)
    {
        var company = new Company { Name = "CT-A" };
        ctx.Companies.Add(company);
        await ctx.SaveChangesAsync();
        return company.Id;
    }

    private static async Task<Guid> SeedActorAsync(AppDbContext ctx, Guid companyId)
    {
        var actor = new User { Username = "admin", Email = "admin@t.local", FirstName = "Admin", LastName = "A", CompanyId = companyId };
        ctx.Users.Add(actor);
        await ctx.SaveChangesAsync();
        return actor.Id;
    }

    private static CreateUserCommandHandler CreateHandler(AppDbContext ctx, TestHelpers.FakeKeycloakService keycloak, Guid actorId)
        => new(ctx, keycloak, TestHelpers.CreateActionLogService(ctx, actorId), NullLogger<CreateUserCommandHandler>.Instance);

    private static UpdateUserCommandHandler UpdateHandler(AppDbContext ctx, TestHelpers.FakeKeycloakService keycloak, Guid actorId)
        => new(ctx, keycloak, TestHelpers.CreateActionLogService(ctx, actorId), NullLogger<UpdateUserCommandHandler>.Instance);

    private static DeleteUserCommandHandler DeleteHandler(AppDbContext ctx, TestHelpers.FakeKeycloakService keycloak, Guid actorId)
        => new(ctx, keycloak, TestHelpers.CreateActionLogService(ctx, actorId), NullLogger<DeleteUserCommandHandler>.Instance);

    // ==================== CREATE ====================

    [Fact]
    public async Task CreateUser_SyncsKeycloak_SavesUser_AndLogsCreateWithCompanyId()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(CreateUser_SyncsKeycloak_SavesUser_AndLogsCreateWithCompanyId));
        var companyId = await SeedCompanyAsync(ctx);
        await SeedActorAsync(ctx, companyId);
        var keycloak = new TestHelpers.FakeKeycloakService();
        var handler = CreateHandler(ctx, keycloak, ActorId);

        var result = await handler.Handle(new CreateUserCommand
        {
            Username = "nv.a", Email = "NVA@Test.local", FirstName = "Nguyen", LastName = "Van A",
            IsActive = true, IsSuperUser = false, CompanyId = companyId
        }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, keycloak.CreateCalls);
        var user = await ctx.Users.SingleAsync(u => u.Username == "nv.a");
        Assert.Equal("nva@test.local", user.Email); // trimmed + lower-cased
        Assert.Equal(companyId, user.CompanyId);

        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.User && l.ActionType == ActionType.Create);
        Assert.Equal(ActorId, log.CreatedBy);
        Assert.Equal(companyId, log.CompanyId);
        Assert.Contains("nv.a", log.Note);
        Assert.Contains("username", log.LogMeta);
    }

    [Fact]
    public async Task CreateUser_KeycloakFailure_ReturnsError_NoLocalUser_NoLog()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(CreateUser_KeycloakFailure_ReturnsError_NoLocalUser_NoLog));
        var companyId = await SeedCompanyAsync(ctx);
        await SeedActorAsync(ctx, companyId);
        var keycloak = new TestHelpers.FakeKeycloakService { CreateShouldThrow = true };
        var handler = CreateHandler(ctx, keycloak, ActorId);

        var result = await handler.Handle(new CreateUserCommand
        {
            Username = "nv.b", Email = "b@t.local", FirstName = "B", LastName = "B", CompanyId = companyId
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("KEYCLOAK_ERROR", result.ErrorCode);
        Assert.Empty(await ctx.Users.Where(u => u.Username == "nv.b").ToListAsync());
        Assert.Empty(await ctx.ActionLogs.ToListAsync());
    }

    [Fact]
    public async Task CreateUser_IsSuperUser_AddsToKeycloakSuperUserGroup()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(CreateUser_IsSuperUser_AddsToKeycloakSuperUserGroup));
        var companyId = await SeedCompanyAsync(ctx);
        await SeedActorAsync(ctx, companyId);
        var keycloak = new TestHelpers.FakeKeycloakService();
        var handler = CreateHandler(ctx, keycloak, ActorId);

        var result = await handler.Handle(new CreateUserCommand
        {
            Username = "sup", Email = "sup@t.local", FirstName = "S", LastName = "S",
            IsSuperUser = true, IsActive = true, CompanyId = companyId
        }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, keycloak.AddToSuperUserGroupCalls);
    }

    // ==================== UPDATE ====================

    [Fact]
    public async Task UpdateUser_Changes_LogsUpdateWithChangesMeta()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(UpdateUser_Changes_LogsUpdateWithChangesMeta));
        var companyId = await SeedCompanyAsync(ctx);
        await SeedActorAsync(ctx, companyId);
        var otherCompany = new Company { Name = "CT-B" };
        ctx.Companies.Add(otherCompany);
        await ctx.SaveChangesAsync();
        var user = new User { Username = "nv.c", Email = "old@t.local", FirstName = "Old", LastName = "C", CompanyId = companyId, IsActive = true };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        var keycloak = new TestHelpers.FakeKeycloakService();
        var handler = UpdateHandler(ctx, keycloak, ActorId);

        var result = await handler.Handle(new UpdateUserCommand
        {
            Id = user.Id, FirstName = "New", LastName = "C", Email = "new@t.local",
            IsSuperUser = false, IsActive = false, CompanyId = otherCompany.Id,
            DepartmentId = null, LocationId = null
        }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, keycloak.UpdateCalls);
        var updated = await ctx.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal("New", updated.FirstName);
        Assert.False(updated.IsActive);

        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.User && l.ActionType == ActionType.Update);
        Assert.Equal(ActorId, log.CreatedBy);
        Assert.Equal(otherCompany.Id, log.CompanyId);
        Assert.NotNull(log.LogMeta);

        using var doc = JsonDocument.Parse(log.LogMeta!);
        var changes = doc.RootElement.GetProperty("changes");
        Assert.Equal("old@t.local", changes.GetProperty("email").GetProperty("old").GetString());
        Assert.Equal("new@t.local", changes.GetProperty("email").GetProperty("new").GetString());
        Assert.Equal(companyId.ToString(), changes.GetProperty("companyId").GetProperty("old").GetString());
        Assert.Equal(otherCompany.Id.ToString(), changes.GetProperty("companyId").GetProperty("new").GetString());
        Assert.Equal(true, changes.GetProperty("isActive").GetProperty("old").GetBoolean());
        Assert.Equal(false, changes.GetProperty("isActive").GetProperty("new").GetBoolean());
    }

    [Fact]
    public async Task UpdateUser_NotFound_ReturnsError()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(UpdateUser_NotFound_ReturnsError));
        var companyId = await SeedCompanyAsync(ctx);
        await SeedActorAsync(ctx, companyId);
        var keycloak = new TestHelpers.FakeKeycloakService();
        var handler = UpdateHandler(ctx, keycloak, ActorId);

        var result = await handler.Handle(new UpdateUserCommand
        {
            Id = Guid.NewGuid(), FirstName = "X", LastName = "Y", Email = "x@t.local",
            IsSuperUser = false, IsActive = true
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("USER_NOT_FOUND", result.ErrorCode);
        Assert.Empty(await ctx.ActionLogs.ToListAsync());
    }

    // ==================== DELETE (soft deactivate) ====================

    [Fact]
    public async Task DeleteUser_Deactivates_LogsDelete_AndDisablesInKeycloak()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(DeleteUser_Deactivates_LogsDelete_AndDisablesInKeycloak));
        var companyId = await SeedCompanyAsync(ctx);
        await SeedActorAsync(ctx, companyId);
        var user = new User { Username = "nv.d", Email = "d@t.local", FirstName = "D", LastName = "D", CompanyId = companyId, IsActive = true };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        var keycloak = new TestHelpers.FakeKeycloakService();
        var handler = DeleteHandler(ctx, keycloak, ActorId);

        var result = await handler.Handle(new DeleteUserCommand(user.Id), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, keycloak.DisableCalls);
        var deactivated = await ctx.Users.SingleAsync(u => u.Id == user.Id);
        Assert.False(deactivated.IsActive); // soft delete — row stays for history

        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.User && l.ActionType == ActionType.Delete);
        Assert.Equal(ActorId, log.CreatedBy);
        Assert.Equal(companyId, log.CompanyId);
        Assert.Contains("nv.d", log.Note);
    }

    [Fact]
    public async Task DeleteUser_NotFound_ReturnsError()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(DeleteUser_NotFound_ReturnsError));
        var companyId = await SeedCompanyAsync(ctx);
        await SeedActorAsync(ctx, companyId);
        var keycloak = new TestHelpers.FakeKeycloakService();
        var handler = DeleteHandler(ctx, keycloak, ActorId);

        var result = await handler.Handle(new DeleteUserCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("USER_NOT_FOUND", result.ErrorCode);
        Assert.Empty(await ctx.ActionLogs.ToListAsync());
    }
}

