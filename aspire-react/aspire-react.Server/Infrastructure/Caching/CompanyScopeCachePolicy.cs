using aspire_react.Server.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace aspire_react.Server.Infrastructure.Caching;

/// <summary>
/// Output-cache policy for the scoped <c>GET /companies</c> endpoint (Task V).
/// <para>
/// Unlike the other reference-data groups (which are global and safe to share), <c>/companies</c>
/// is now <b>company-scoped</b>: a regular user only sees their own company subtree, while a
/// Superuser sees the full tree. Sharing one cached response across users would leak one user's
/// scoped view to another (the exact cross-company leak warned about in Task P backlog 33).
/// </para>
/// <para>
/// To isolate caches by scope, this policy appends a per-request <c>VaryByValues["company_scope"]</c>
/// to the cache key (the same mechanism as the documented <c>VaryByValue</c>): Superuser / no-company
/// regular user → <c>"all"</c>; regular user with company X → <c>"c:&lt;X&gt;"</c>. Different scopes
/// therefore produce different Redis keys and never read each other's entries.
/// </para>
/// <para>
/// Invalidation stays correct for ALL variants: every scope variant carries the <c>ref:companies</c>
/// tag (set via <c>[OutputCache(Tags=...)]</c>), so <see cref="ICacheInvalidator.InvalidateCompaniesAsync"/>
/// (which evicts by that tag) removes every scope key at once — no stale variant is left behind.
/// </para>
/// </summary>
public sealed class CompanyScopeCachePolicy : IOutputCachePolicy
{
    public static readonly CompanyScopeCachePolicy Instance = new();

    private CompanyScopeCachePolicy() { }

    public async ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        var isCacheableMethod = HttpMethods.IsGet(context.HttpContext.Request.Method)
            || HttpMethods.IsHead(context.HttpContext.Request.Method);

        context.EnableOutputCaching = true;
        context.AllowCacheLookup = isCacheableMethod;
        context.AllowCacheStorage = isCacheableMethod;
        context.AllowLocking = true;
        context.ResponseExpirationTimeSpan = ReferenceDataCachePolicy.Expiration;
        // Vary by every query-string key (consistency with the shared policy).
        context.CacheVaryByRules.QueryKeys = "*";

        // Scope key is part of the cache key → each company scope gets its own cache entry.
        // Must mirror CompaniesController.GetAll's data selection so the key and payload agree.
        var scopeKey = await ResolveScopeKeyAsync(context.HttpContext);
        context.CacheVaryByRules.VaryByValues["company_scope"] = scopeKey;
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

    /// <summary>
    /// Resolves the cache-scope key. Mirrors <see cref="CompaniesController.GetAll"/>: Superuser or a
    /// regular user without a company sees the full tree (<c>"all"</c>); a regular user with a company
    /// sees only that company's subtree (<c>"c:&lt;id&gt;"</c>). Kept in sync so cache key == payload.
    /// </summary>
    private static async Task<string> ResolveScopeKeyAsync(HttpContext httpContext)
    {
        var scopeService = httpContext.RequestServices?.GetService(typeof(ICompanyScopeService)) as ICompanyScopeService;
        if (scopeService == null) return "unknown";

        if (scopeService.IsSuperUser()) return "all";

        var companyId = await scopeService.GetCurrentUserCompanyIdAsync();
        return companyId.HasValue ? "c:" + companyId.Value : "all";
    }
}
