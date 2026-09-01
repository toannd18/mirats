using aspire_react.Server.Application.Common.Interfaces;
using MediatR;

namespace aspire_react.Server.Application.Common.Behaviors;

/// <summary>
/// [Giai đoạn 1.5 — M1 sibling] Pipeline behavior that evicts output-cache tags for commands
/// implementing <see cref="ICacheInvalidatingCommand{TResponse}"/> — registered FIRST
/// (outermost) in <c>AddOpenBehavior</c> so its post-phase runs LAST:
/// <c>Validation → ActionLog.tx → Handler → ActionLog(log+commit) → CacheInvalidation(evict)</c>.
/// </summary>
/// <para>
/// WHY outermost (differs from a naive "inner-most" reading of pipeline order — deliberate):
/// (1) eviction must happen AFTER the ActionLogBehavior commits, otherwise a concurrent GET could
/// re-populate the cache with the OLD data between evict and commit (stale-until-TTL race);
/// (2) if eviction were inside the ActionLog transaction, an eviction failure would roll back the
/// committed data — outer placement matches the legacy controller semantics (data already saved,
/// invalidation failure surfaces as 500 with data intact). Effective order therefore matches the
/// legacy controllers exactly: data save → log → invalidate last.
/// </para>
/// <para>
/// Non-marked requests pass through untouched (zero cost). The handler throwing means
/// <c>next()</c> throws inside this behavior — the eviction code after <c>next()</c> never runs,
/// mirroring the "only on success" principle of ActionLogBehavior.
/// </para>
public sealed class CacheInvalidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ICacheTagEvictor _evictor;

    public CacheInvalidationBehavior(ICacheTagEvictor evictor)
    {
        _evictor = evictor;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Opt-in: non-marked requests are 100% pass-through.
        if (request is not ICacheInvalidatingCommand<TResponse> invalidating)
            return await next();

        var response = await next(); // handler already succeeded when we get here (exceptions propagate)

        if (invalidating.ShouldInvalidateCache(response))
            await _evictor.EvictTagsAsync(invalidating.CacheTagsToInvalidate, cancellationToken);

        return response;
    }
}
