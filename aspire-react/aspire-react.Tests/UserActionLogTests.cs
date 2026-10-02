using System.Text.Json;
using aspire_react.Server.Application.Common.Behaviors;
using aspire_react.Server.Application.Users.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Authentication;
using aspire_react.Server.Infrastructure.Authorization;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// ST9/F41 — User CRUD ActionLog coverage (ST5): CreateUserCommand / UpdateUserCommand /
/// DeleteUserCommand must write the matching ActionLog row (with CompanyId and the
/// { changes: { field: { old, new } } } meta for updates). [AUTH Phase 4/5] handlers are
/// local-only — the former Keycloak mocks were removed together with the sync code.
///
/// [FIX BUG-M 2026-10-02] The three commands are now <c>ILoggableCommand</c> and the controller no
/// longer logs a second time, so the log tests drive each command through the REAL
/// <see cref="ActionLogBehavior{TRequest,TResponse}"/> (the only path that writes the entry now) —
/// same assertions, and "exactly one log per write" is enforced by Assert.Single-style queries.
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

    // Handlers are built without an ActionLog service: the audit entry comes from ActionLogBehavior
    // (ILoggableCommand). A superuser company scope keeps these tests focused on the log/password
    // shape (the company-scoping rules have their own dedicated test files).
    private static CreateUserCommandHandler CreateHandler(AppDbContext ctx)
        => new(ctx, new PasswordHasherService(), new TestHelpers.FakeScope { Super = true },
            NullLogger<CreateUserCommandHandler>.Instance);

    private static UpdateUserCommandHandler UpdateHandler(AppDbContext ctx)
        => new(ctx, new TestHelpers.FakeScope { Super = true }, new PermissionLockoutGuard(ctx),
            NullLogger<UpdateUserCommandHandler>.Instance);

    private static DeleteUserCommandHandler DeleteHandler(AppDbContext ctx)
        => new(ctx, new TestHelpers.FakeScope { Super = true }, new PermissionLockoutGuard(ctx),
            NullLogger<DeleteUserCommandHandler>.Instance);

    // ==================== CREATE ====================

    [Fact]
    public async Task CreateUser_SetsPasswordHash_MustChange_AndLogsCreateWithCompanyId()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(CreateUser_SetsPasswordHash_MustChange_AndLogsCreateWithCompanyId));
        var companyId = await SeedCompanyAsync(ctx);
        await SeedActorAsync(ctx, companyId);
        var handler = CreateHandler(ctx);
        var actionLog = TestHelpers.CreateActionLogService(ctx, ActorId);
        var behavior = new ActionLogBehavior<CreateUserCommand, CreateUserResult>(actionLog, ctx);

        var cmd = new CreateUserCommand
        {
            Username = "nv.a",
            Email = "NVA@Test.local",
            FirstName = "Nguyen",
            LastName = "Van A",
            Password = "Init#Pass2026",
            IsActive = true,
            IsSuperUser = false,
            CompanyId = companyId,
            CurrentUserId = ActorId
        };
        var result = await behavior.Handle(cmd, ct => handler.Handle(cmd, ct), CancellationToken.None);

        Assert.True(result.Success);
        var user = await ctx.Users.SingleAsync(u => u.Username == "nv.a");
        Assert.Equal("nva@test.local", user.Email); // trimmed + lower-cased
        Assert.Equal(companyId, user.CompanyId);
        // [AUTH Phase 4] local-only creation: hash stored, MUST-change enforced, no Keycloak.
        Assert.False(string.IsNullOrEmpty(user.PasswordHash));
        Assert.Equal(PasswordVerifyResult.Success, new PasswordHasherService().Verify("Init#Pass2026", user.PasswordHash!));
        Assert.True(user.MustChangePassword);

        // [FIX BUG-M] Exactly ONE log entry (the controller's second, non-atomic log is gone).
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.User && l.ActionType == ActionType.Create);
        Assert.Equal(ActorId, log.CreatedBy);
        Assert.Equal(companyId, log.CompanyId);
        Assert.Contains("nv.a", log.Note);
        Assert.Contains("username", log.LogMeta);
    }

    [Fact]
    public async Task CreateUser_ShortPassword_Rejected_NoLocalUser_NoLog()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(CreateUser_ShortPassword_Rejected_NoLocalUser_NoLog));
        var companyId = await SeedCompanyAsync(ctx);
        await SeedActorAsync(ctx, companyId);
        var handler = CreateHandler(ctx);
        var actionLog = TestHelpers.CreateActionLogService(ctx, ActorId);
        var behavior = new ActionLogBehavior<CreateUserCommand, CreateUserResult>(actionLog, ctx);

        var cmd = new CreateUserCommand
        {
            Username = "nv.b",
            Email = "b@t.local",
            FirstName = "B",
            LastName = "B",
            Password = "short",
            CompanyId = companyId,
            CurrentUserId = ActorId
        };
        var result = await behavior.Handle(cmd, ct => handler.Handle(cmd, ct), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
        Assert.Empty(await ctx.Users.Where(u => u.Username == "nv.b").ToListAsync());
        Assert.Empty(await ctx.ActionLogs.ToListAsync());
    }

    [Fact]
    public async Task CreateUser_IsSuperUser_LocalFlagOnly_NoKeycloakGroup()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(CreateUser_IsSuperUser_LocalFlagOnly_NoKeycloakGroup));
        var companyId = await SeedCompanyAsync(ctx);
        await SeedActorAsync(ctx, companyId);
        var handler = CreateHandler(ctx);

        var result = await handler.Handle(new CreateUserCommand
        {
            Username = "sup",
            Email = "sup@t.local",
            FirstName = "S",
            LastName = "S",
            Password = "Init#Pass2026",
            IsSuperUser = true,
            IsActive = true,
            CompanyId = companyId,
            CurrentUserId = ActorId
        }, CancellationToken.None);

        Assert.True(result.Success);
        var user = await ctx.Users.SingleAsync(u => u.Username == "sup");
        Assert.True(user.IsSuperUser);
        Assert.True(user.MustChangePassword);
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
        var handler = UpdateHandler(ctx);
        var actionLog = TestHelpers.CreateActionLogService(ctx, ActorId);
        var behavior = new ActionLogBehavior<UpdateUserCommand, UpdateUserResult>(actionLog, ctx);

        var cmd = new UpdateUserCommand
        {
            Id = user.Id,
            FirstName = "New",
            LastName = "C",
            Email = "new@t.local",
            IsSuperUser = false,
            IsActive = false,
            CompanyId = otherCompany.Id,
            DepartmentId = null,
            LocationId = null,
            CurrentUserId = ActorId
        };
        var result = await behavior.Handle(cmd, ct => handler.Handle(cmd, ct), CancellationToken.None);

        Assert.True(result.Success);
        var updated = await ctx.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal("New", updated.FirstName);
        Assert.False(updated.IsActive);

        // [FIX BUG-M] Exactly ONE log entry (no duplicate from the controller).
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
        Assert.True(changes.GetProperty("isActive").GetProperty("old").GetBoolean());
        Assert.False(changes.GetProperty("isActive").GetProperty("new").GetBoolean());
    }

    [Fact]
    public async Task UpdateUser_NotFound_ReturnsError()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(UpdateUser_NotFound_ReturnsError));
        var companyId = await SeedCompanyAsync(ctx);
        await SeedActorAsync(ctx, companyId);
        var handler = UpdateHandler(ctx);

        var result = await handler.Handle(new UpdateUserCommand
        {
            Id = Guid.NewGuid(),
            FirstName = "X",
            LastName = "Y",
            Email = "x@t.local",
            IsSuperUser = false,
            IsActive = true,
            CurrentUserId = ActorId
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("USER_NOT_FOUND", result.ErrorCode);
        Assert.Empty(await ctx.ActionLogs.ToListAsync());
    }

    // ==================== DELETE (soft deactivate) ====================

    [Fact]
    public async Task DeleteUser_Deactivates_LogsDelete_LocalOnly()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(DeleteUser_Deactivates_LogsDelete_LocalOnly));
        var companyId = await SeedCompanyAsync(ctx);
        await SeedActorAsync(ctx, companyId);
        var user = new User { Username = "nv.d", Email = "d@t.local", FirstName = "D", LastName = "D", CompanyId = companyId, IsActive = true };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        var handler = DeleteHandler(ctx);
        var actionLog = TestHelpers.CreateActionLogService(ctx, ActorId);
        var behavior = new ActionLogBehavior<DeleteUserCommand, DeleteUserResult>(actionLog, ctx);

        var cmd = new DeleteUserCommand(user.Id, ActorId);
        var result = await behavior.Handle(cmd, ct => handler.Handle(cmd, ct), CancellationToken.None);

        Assert.True(result.Success);
        var deactivated = await ctx.Users.SingleAsync(u => u.Id == user.Id);
        Assert.False(deactivated.IsActive); // soft delete — row stays for history

        // [FIX BUG-M] Exactly ONE log entry.
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
        var handler = DeleteHandler(ctx);

        var result = await handler.Handle(new DeleteUserCommand(Guid.NewGuid(), ActorId), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("USER_NOT_FOUND", result.ErrorCode);
        Assert.Empty(await ctx.ActionLogs.ToListAsync());
    }
}
