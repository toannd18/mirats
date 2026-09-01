namespace aspire_react.Server.Application.Common.Interfaces;

/// <summary>
/// [Giai đoạn 1.5] Application-owned contract for output-cache tag eviction, consumed by
/// <see cref="Common.Behaviors.CacheInvalidationBehavior{TRequest,TResponse}"/>. Implemented in
/// Infrastructure (wraps the Redis-backed <c>IOutputCacheStore.EvictByTagAsync</c>) — the narrow
/// interface keeps the Application layer free of any ASP.NET Core OutputCache reference.
/// Single existing production consumer today: reference-data tags (see CacheTags).
/// </summary>
public interface ICacheTagEvictor
{
    /// <summary>Evicts every output-cache entry tagged with any of <paramref name="tags"/>.</summary>
    Task EvictTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default);
}
