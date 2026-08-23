using System.Security.Claims;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace aspire_react.Server.Infrastructure.Services;

public interface ICompanyScopeService
{
    /// <summary>
    /// ⚠️ [CS-7 post-fix note, 2026-08-23] PLACEHOLDER — currently ALWAYS returns an empty list
    /// (see implementation below). Do NOT use it for any company-isolation check: a
    /// "userCompanyIds.Count == 0" guard is always true and silently disables the gate (this exact
    /// bug leaked /action-logs/by-system cross-company until the 2026-08-23 fix). For scoping use
    /// <see cref="GetCurrentUserCompanyIdAsync"/> instead. Kept only because AppDbContext's global
    /// query filter (already a documented no-op) and SystemsController still reference it.
    /// </summary>
    Task<List<Guid>> GetUserCompanyIdsAsync();
    bool IsSuperUser();
    /// <summary>
    /// Returns the current user's scope as a company id, with THREE distinct states
    /// [SEC-FIX JIT-COMPANYLESS, 2026-08-23]:
    ///   - Superuser / unresolved principal  → <c>null</c> (callers treat null as "see everything").
    ///   - Regular user WITH a company      → their <see cref="User.CompanyId"/>.
    ///   - Regular user WITHOUT a company (JIT-created, admin has not assigned one yet)
    ///     → <c>Guid.Empty</c> sentinel, so callers' existing "null = sees all" guards do NOT
    ///     grant them cross-company access — they only see company-less (floater) records until
    ///     an admin assigns a company via the User management UI.
    /// </summary>
    Task<Guid?> GetCurrentUserCompanyIdAsync();
    /// <summary>
    /// [Task IMPORT-T5] Validates that <paramref name="companyId"/> lies inside the acting user's
    /// REAL company scope (server-side — never trust a client-supplied id, Task L2 principle):
    /// the company must EXIST, and a regular user with a company may only target that company or
    /// any of its descendants (parent may import for children); superuser or a company-less regular
    /// user may target any company (matching the Task V CompaniesController.GetAll convention).
    /// </summary>
    Task<bool> IsCompanyIdInUserScopeAsync(Guid companyId);
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

        // [SEC-FIX JIT-COMPANYLESS, 2026-08-23] A regular user whose local record has NO CompanyId
        // (JIT-created on first login, admin has not assigned a company yet) is a distinct state
        // from a Superuser. Return Guid.Empty instead of null so the widespread
        // "userCompanyId == null → see everything" pattern does NOT treat them as unrestricted:
        // with Guid.Empty, filters like `x.CompanyId == null || x.CompanyId == userCompanyId.Value`
        // collapse to "company-less records only". Superuser still gets null → sees all.
        var companyId = await db.Users.AsNoTracking()
            .Where(u => u.Id == localUserId)
            .Select(u => u.CompanyId)
            .FirstOrDefaultAsync();

        return companyId ?? Guid.Empty;
    }

    public async Task<bool> IsCompanyIdInUserScopeAsync(Guid companyId)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var user = httpContext?.User;
        if (user == null || user.Identity?.IsAuthenticated != true) return false;

        // Resolve the request-scoped DbContext via RequestServices to avoid a circular DI
        // dependency (AppDbContext itself depends on ICompanyScopeService for its query filters).
        var db = httpContext.RequestServices?.GetService(typeof(AppDbContext)) as AppDbContext;
        if (db == null) return false;

        // The company must exist (a made-up id must never pass).
        if (!await db.Companies.AsNoTracking().AnyAsync(c => c.Id == companyId)) return false;

        // Superuser has no company restriction.
        if (IsSuperUser()) return true;

        if (!Guid.TryParse(user.FindFirstValue("local_user_id"), out var localUserId) || localUserId == Guid.Empty)
            return false;

        var userCompanyId = await db.Users.AsNoTracking()
            .Where(u => u.Id == localUserId)
            .Select(u => u.CompanyId)
            .FirstOrDefaultAsync();

        // [SEC-FIX JIT-COMPANYLESS, 2026-08-23] A regular user WITHOUT a company (JIT-created,
        // not yet assigned) may NOT target any specific company — imports require a concrete
        // companyId (COMPANY_REQUIRED) and floater-to-floater does not apply here. They must be
        // assigned a company first (via the User management UI). Superuser was already handled
        // above (IsSuperUser → true), so reaching here with a null company always denies.
        if (userCompanyId == null) return false;

        if (userCompanyId.Value == companyId) return true;

        // Parent company → may target any descendant. BFS the company tree.
        var queue = new Queue<Guid>();
        queue.Enqueue(userCompanyId.Value);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var children = await db.Companies.AsNoTracking()
                .Where(c => c.ParentId == current)
                .Select(c => c.Id)
                .ToListAsync();
            foreach (var childId in children)
            {
                if (childId == companyId) return true;
                queue.Enqueue(childId);
            }
        }
        return false;
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
        // ⚠️ [CS-7 note] This placeholder caused the by-system gate NO-OP fixed on 2026-08-23.
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