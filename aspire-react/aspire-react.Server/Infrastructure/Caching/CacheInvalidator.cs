using Microsoft.AspNetCore.OutputCaching;

namespace aspire_react.Server.Infrastructure.Caching;

/// <summary>
/// Centralized output-cache invalidation for the reference-data groups cached via
/// <see cref="ReferenceDataCachePolicy"/> (Task P invalidation, Task P-addendum).
/// <para>
/// Controllers MUST call this instead of touching <see cref="IOutputCacheStore"/> directly, so a
/// single file owns every tag/eviction decision. This avoids the "each place does it its own way"
/// drift seen with ActionLog (Task N). Each group invalidates ONLY its own tag (editing a Supplier
/// never evicts Categories), unless a technical reason forces a wider eviction — documented inline.
/// </para>
/// </summary>
public interface ICacheInvalidator
{
    /// <summary>Evicts the Categories reference-data cache (GET /categories).</summary>
    Task InvalidateCategoriesAsync(CancellationToken ct = default);

    /// <summary>Evicts the Manufacturers reference-data cache (GET /manufacturers).</summary>
    Task InvalidateManufacturersAsync(CancellationToken ct = default);

    /// <summary>Evicts the Suppliers reference-data cache (GET /suppliers).</summary>
    Task InvalidateSuppliersAsync(CancellationToken ct = default);

    /// <summary>Evicts the Companies reference-data cache (GET /companies).</summary>
    Task InvalidateCompaniesAsync(CancellationToken ct = default);
}

/// <inheritdoc cref="ICacheInvalidator"/>
public sealed class CacheInvalidator : ICacheInvalidator
{
    private readonly IOutputCacheStore _store;

    public CacheInvalidator(IOutputCacheStore store) => _store = store;

    public Task InvalidateCategoriesAsync(CancellationToken ct = default)
        => _store.EvictByTagAsync(CacheTags.Categories, ct).AsTask();

    public Task InvalidateManufacturersAsync(CancellationToken ct = default)
        => _store.EvictByTagAsync(CacheTags.Manufacturers, ct).AsTask();

    public Task InvalidateSuppliersAsync(CancellationToken ct = default)
        => _store.EvictByTagAsync(CacheTags.Suppliers, ct).AsTask();

    public Task InvalidateCompaniesAsync(CancellationToken ct = default)
        => _store.EvictByTagAsync(CacheTags.Companies, ct).AsTask();
}

/// <summary>Output-cache tags for reference-data groups. Must match the <c>Tags</c> on each
/// <c>[OutputCache]</c> attribute so <see cref="IOutputCacheStore.EvictByTagAsync"/> hits the entry.</summary>
public static class CacheTags
{
    public const string Categories = "ref:categories";
    public const string Manufacturers = "ref:manufacturers";
    public const string Suppliers = "ref:suppliers";
    public const string Companies = "ref:companies";
}
