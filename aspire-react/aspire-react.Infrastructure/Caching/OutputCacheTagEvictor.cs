using aspire_react.Server.Application.Common.Interfaces;
using Microsoft.AspNetCore.OutputCaching;

namespace aspire_react.Server.Infrastructure.Caching;

/// <summary>
/// [Giai đoạn 1.5] Application-side <see cref="ICacheTagEvictor"/> contract implemented over the
/// Redis-backed ASP.NET Core output-cache store. Used exclusively by
/// CacheInvalidationBehavior — feature-specific invalidation keeps flowing through
/// <see cref="ICacheInvalidator"/> (typed methods) until the owning controllers migrate.
/// </summary>
public sealed class OutputCacheTagEvictor : ICacheTagEvictor
{
    private readonly IOutputCacheStore _store;

    public OutputCacheTagEvictor(IOutputCacheStore store) => _store = store;

    public async Task EvictTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        foreach (var tag in tags)
            await _store.EvictByTagAsync(tag, cancellationToken);
    }
}
