using aspire_react.Server.Application.Common;
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
/// [Giai đoạn 1.5] CacheTags constants moved verbatim to Application/Common/CacheTags.cs so
/// Application commands (ICacheInvalidatingCommand) can reference them — values unchanged.
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
