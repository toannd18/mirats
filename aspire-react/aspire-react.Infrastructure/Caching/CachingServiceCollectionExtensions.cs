using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace aspire_react.Server.Infrastructure.Caching;

/// <summary>
/// Registers Redis-backed ASP.NET Core output caching (Task P). Uses
/// <c>Aspire.StackExchange.Redis.OutputCaching</c>'s <c>AddRedisOutputCache</c>, which registers
/// <c>IOutputCacheStore</c> backed by Redis (connection "cache") and wires up its own health check
/// + telemetry. Consumed via <c>[OutputCache(PolicyName = "RefData")]</c> attributes on reference-data
/// GET endpoints and <c>app.UseOutputCache()</c> in the pipeline (placed after UseAuthorization so
/// unauthorized requests short-circuit before the cache middleware).
/// </summary>
public static class CachingServiceCollectionExtensions
{
    public static IHostApplicationBuilder AddRedisCaching(this IHostApplicationBuilder builder)
    {
        // connectionName must match the Redis resource name declared in the AppHost ("cache").
        builder.AddRedisOutputCache(connectionName: "cache");

        // Register the policy that caches AUTHENTICATED reference-data responses. The default
        // output-cache policy never caches authenticated requests. Configure OutputCacheOptions
        // WITHOUT re-calling AddOutputCache (that would re-register the in-memory store and override
        // the Redis store registered by AddRedisOutputCache above).
        builder.Services.Configure<OutputCacheOptions>(o =>
        {
            o.AddPolicy("RefData", ReferenceDataCachePolicy.Instance);
            // Task V: /companies is company-scoped, so it needs a policy that varies the cache key
            // by the user's company scope (Superuser → "all", regular user → "c:<companyId>").
            o.AddPolicy("RefDataCompanyScope", CompanyScopeCachePolicy.Instance);
        });

        // Centralized invalidation for the cached reference-data groups (Task P invalidation).
        // Backed by the same Redis IOutputCacheStore registered above; controllers evict through this.
        builder.Services.AddSingleton<ICacheInvalidator, CacheInvalidator>();

        return builder;
    }
}
