using System.Security.Claims;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace aspire_react.Server.Infrastructure.Services;

public interface ICompanyScopeService
{
    Task<List<Guid>> GetUserCompanyIdsAsync();
    bool IsSuperUser();
    /// <summary>
    /// Returns the current user's local CompanyId (null for Superuser or when not resolvable).
    /// Used to scope records by company for regular users (e.g. Asset Maintenance visibility).
    /// </summary>
    Task<Guid?> GetCurrentUserCompanyIdAsync();
}

public class CompanyScopeService : ICompanyScopeService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public CompanyScopeService(IHttpContextAccessor httpContextAccessor, IMemoryCache cache)
    {
        _httpContextAccessor = httpContextAccessor;
        _cache = cache;
    }

    public bool IsSuperUser()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null || user.Identity?.IsAuthenticated != true) return false;
        return RealmAccessHelper.IsSuperUser(user);
    }

    public async Task<Guid?> GetCurrentUserCompanyIdAsync()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var user = httpContext?.User;
        if (user == null || user.Identity?.IsAuthenticated != true) return null;
        // Superuser has no company restriction.
        if (IsSuperUser()) return null;

        // local_user_id is the local DB user id stamped by JIT provisioning (Keycloak sub ≠ local id).
        if (!Guid.TryParse(user.FindFirstValue("local_user_id"), out var localUserId) || localUserId == Guid.Empty)
            return null;

        // Resolve the request-scoped DbContext via RequestServices to avoid a circular DI
        // dependency (AppDbContext itself depends on ICompanyScopeService for its query filters).
        var db = httpContext.RequestServices?.GetService(typeof(AppDbContext)) as AppDbContext;
        if (db == null) return null;

        return await db.Users.AsNoTracking()
            .Where(u => u.Id == localUserId)
            .Select(u => u.CompanyId)
            .FirstOrDefaultAsync();
    }

    public Task<List<Guid>> GetUserCompanyIdsAsync()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
            return Task.FromResult(new List<Guid>());

        // Superuser sees all companies (empty list = no filter)
        if (IsSuperUser())
            return Task.FromResult(new List<Guid>());

        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
            return Task.FromResult(new List<Guid>());

        var cacheKey = $"company_ids_{userId}";
        if (_cache.TryGetValue(cacheKey, out List<Guid>? cachedIds) && cachedIds != null)
            return Task.FromResult(cachedIds);

        // For now, return empty list — companies will be loaded when User entity
        // has company assignments (UserCompanies table). Placeholder for Phase 5 expansion.
        // In current implementation, admin/superuser bypasses filters,
        // and regular users have no company restrictions until FMCS is fully configured.
        var companyIds = new List<Guid>();
        _cache.Set(cacheKey, companyIds, CacheDuration);
        return Task.FromResult(companyIds);
    }

    private Guid GetCurrentUserId()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null) return Guid.Empty;
        // Local DB user id stamped by JIT provisioning (Keycloak sub ≠ local id). It is the ONLY
        // reliable id — `sub` is the Keycloak subject id, not the local DB user id, so it is never
        // used as a DB key (removed per ST7/F1; fallback would silently return the wrong id).
        if (Guid.TryParse(user.FindFirstValue("local_user_id"), out var local)) return local;
        return Guid.Empty;
    }
}