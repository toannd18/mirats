using aspire_react.Server.Application.Assets.Commands;
using aspire_react.Server.Application.Common.Behaviors;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// Task L â€” unit tests for the new <see cref="ValidationBehavior{TRequest,TResponse}"/>: it must run
/// registered validators and throw on failure (so the API maps them to a clean 400), and pass through
/// when valid. The behavior itself is exercised directly (MediatR's real pipeline is covered by API tests).
/// </summary>
public class ValidationBehaviorTests
{
    private static AppDbContext CreateContext(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(options, new TestHelpers.SuperUserScope());
    }

    [Fact]
    public async Task Behavior_RunsValidator_ThrowsValidationException_OnDuplicateAssetTag()
    {
        await using var ctx = CreateContext(nameof(Behavior_RunsValidator_ThrowsValidationException_OnDuplicateAssetTag));
        ctx.Assets.Add(new Asset { AssetTag = "DUP", Name = "Existing" });
        await ctx.SaveChangesAsync();

        var behavior = new ValidationBehavior<CreateAssetCommand, AssetResult>(
            new[] { new CreateAssetCommandValidator(ctx) });
        var cmd = new CreateAssetCommand { AssetTag = "DUP", Name = "New", CurrentUserId = Guid.NewGuid() };

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(
            () => behavior.Handle(cmd, _ => Task.FromResult(new AssetResult(true, "ok")), CancellationToken.None));
    }

    [Fact]
    public async Task Behavior_EmptyTag_FailsValidation()
    {
        await using var ctx = CreateContext(nameof(Behavior_EmptyTag_FailsValidation));
        var behavior = new ValidationBehavior<CreateAssetCommand, AssetResult>(
            new[] { new CreateAssetCommandValidator(ctx) });
        var cmd = new CreateAssetCommand { AssetTag = "", Name = "New", CurrentUserId = Guid.NewGuid() };

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(
            () => behavior.Handle(cmd, _ => Task.FromResult(new AssetResult(true, "ok")), CancellationToken.None));
    }

    [Fact]
    public async Task Behavior_PassesThrough_WhenValid()
    {
        await using var ctx = CreateContext(nameof(Behavior_PassesThrough_WhenValid));
        var behavior = new ValidationBehavior<CreateAssetCommand, AssetResult>(
            new[] { new CreateAssetCommandValidator(ctx) });
        var cmd = new CreateAssetCommand { AssetTag = "UNIQUE-001", Name = "New", CurrentUserId = Guid.NewGuid() };

        var result = await behavior.Handle(cmd, _ => Task.FromResult(new AssetResult(true, "ok")), CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal("ok", result.Message);
    }

    [Fact]
    public async Task Behavior_NoValidators_PassesThrough()
    {
        await using var ctx = CreateContext(nameof(Behavior_NoValidators_PassesThrough));
        var behavior = new ValidationBehavior<CreateAssetCommand, AssetResult>(Array.Empty<IValidator<CreateAssetCommand>>());
        var cmd = new CreateAssetCommand { AssetTag = "X", Name = "Y", CurrentUserId = Guid.NewGuid() };

        var result = await behavior.Handle(cmd, _ => Task.FromResult(new AssetResult(true, "ok")), CancellationToken.None);
        Assert.True(result.Success);
    }
}
