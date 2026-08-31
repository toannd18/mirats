using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Primitives;

namespace aspire_react.Server.Infrastructure.Caching;

/// <summary>
/// Output-cache policy for the reference-data endpoints (Task P).
/// <para>
/// The ASP.NET Core DEFAULT output-cache policy never caches responses to AUTHENTICATED requests
/// (rule: "Responses to authenticated requests aren't cached"). All 5 cached endpoints here are
/// <c>[Authorize]</c>-gated, so the default policy would never cache them. This policy explicitly
/// enables caching for authenticated GET/HEAD requests.
/// </para>
/// <para>
/// SAFETY: it is safe to share one cached response across all users here because all 5 endpoints
/// return GLOBAL data — categories/manufacturers/suppliers are not company-scoped (no CompanyId),
/// the permission catalog is static, and /companies returns every company for any authorized user.
/// The response is identical for every authorized user. Unauthorized users are short-circuited by
/// <c>UseAuthorization()</c> BEFORE <c>UseOutputCache()</c> in the pipeline, so the cache is never
/// consulted for them (no cross-user / privilege leak). Only HTTP 200 responses without Set-Cookie
/// are stored; the cache key varies by the full query string (e.g. /categories?type=Asset).
/// </para>
/// </summary>
public sealed class ReferenceDataCachePolicy : IOutputCachePolicy
{
    public static readonly ReferenceDataCachePolicy Instance = new();

    /// <summary>Time-to-live for cached reference data (300 s = 5 min).</summary>
    public static readonly TimeSpan Expiration = TimeSpan.FromSeconds(300);

    private ReferenceDataCachePolicy() { }

    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        var isCacheableMethod = HttpMethods.IsGet(context.HttpContext.Request.Method)
            || HttpMethods.IsHead(context.HttpContext.Request.Method);

        context.EnableOutputCaching = true;
        context.AllowCacheLookup = isCacheableMethod;
        context.AllowCacheStorage = isCacheableMethod;
        context.AllowLocking = true;
        context.ResponseExpirationTimeSpan = Expiration;
        // Vary by every query-string key (categories uses ?type=) — different values = different entries.
        context.CacheVaryByRules.QueryKeys = "*";

        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;
        if (!StringValues.IsNullOrEmpty(response.Headers.SetCookie)
            || response.StatusCode != StatusCodes.Status200OK)
        {
            context.AllowCacheStorage = false;
        }
        return ValueTask.CompletedTask;
    }
}
