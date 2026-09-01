using aspire_react.Server.Application.Common.Behaviors;
using aspire_react.Server.Application.Common.Interfaces;
using MediatR;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [Giai đoạn 1.5] Unit tests for CacheInvalidationBehavior — the three pipeline rules:
/// marker command → tags evicted after success; non-marker → no eviction; handler throwing →
/// no eviction. The evictor is a recording fake; real-Redis end-to-end is verified separately
/// against the running stack (see G1.5 report).
/// </summary>
public class CacheInvalidationBehaviorTests
{
    private sealed class RecordingEvictor : ICacheTagEvictor
    {
        public List<string[]> Calls { get; } = new();
        public Task EvictTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
        {
            Calls.Add(tags.ToArray());
            return Task.CompletedTask;
        }
    }

    /// <summary>Test command implementing the marker; can be told to throw from its "handler".</summary>
    private sealed class TaggedCommand(string[] tags, bool throwInside) : IRequest<string>, ICacheInvalidatingCommand<string>
    {
        public IEnumerable<string> CacheTagsToInvalidate => tags;
        public bool ThrowInside { get; } = throwInside;
    }

    /// <summary>Test command WITHOUT the marker.</summary>
    private sealed class UntaggedCommand : IRequest<string>;

    [Fact]
    public async Task Behavior_MarkerCommand_EvictsTags_AfterHandlerSuccess()
    {
        var evictor = new RecordingEvictor();
        var behavior = new CacheInvalidationBehavior<TaggedCommand, string>(evictor);
        var cmd = new TaggedCommand(new[] { "ref:categories", "ref:manufacturers" }, throwInside: false);

        var response = await behavior.Handle(cmd, _ => Task.FromResult("ok"), CancellationToken.None);

        Assert.Equal("ok", response);
        var call = Assert.Single(evictor.Calls);
        Assert.Equal(new[] { "ref:categories", "ref:manufacturers" }, call);
    }

    [Fact]
    public async Task Behavior_NonMarkerCommand_DoesNotEvict()
    {
        var evictor = new RecordingEvictor();
        var behavior = new CacheInvalidationBehavior<UntaggedCommand, string>(evictor);

        var response = await behavior.Handle(new UntaggedCommand(), _ => Task.FromResult("ok"), CancellationToken.None);

        Assert.Equal("ok", response);
        Assert.Empty(evictor.Calls);
    }

    [Fact]
    public async Task Behavior_HandlerThrows_DoesNotEvict()
    {
        var evictor = new RecordingEvictor();
        var behavior = new CacheInvalidationBehavior<TaggedCommand, string>(evictor);
        var cmd = new TaggedCommand(new[] { "ref:categories" }, throwInside: true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => behavior.Handle(cmd, _ => throw new InvalidOperationException("boom"), CancellationToken.None));

        Assert.Empty(evictor.Calls);
    }

    [Fact]
    public async Task Behavior_ShouldInvalidateCacheFalse_SkipsEviction()
    {
        var evictor = new RecordingEvictor();
        var behavior = new CacheInvalidationBehavior<SoftFailCommand, string>(evictor);
        var cmd = new SoftFailCommand();

        var response = await behavior.Handle(cmd, _ => Task.FromResult("COMPANY_MISMATCH"), CancellationToken.None);

        Assert.Equal("COMPANY_MISMATCH", response);
        Assert.Empty(evictor.Calls);
    }

    /// <summary>Soft-fail gate: overrides ShouldInvalidateCache to skip eviction for failure responses.</summary>
    private sealed class SoftFailCommand : IRequest<string>, ICacheInvalidatingCommand<string>
    {
        public IEnumerable<string> CacheTagsToInvalidate => new[] { "ref:categories" };
        public bool ShouldInvalidateCache(string response) => response == "ok";
    }
}
