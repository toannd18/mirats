using System.Security.Claims;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace aspire_react.Server.Infrastructure.Services;

// [Giai đoạn 0.1 — F1] Interface ICompanyScopeService moved verbatim to
// Domain/Interfaces/ICompanyScopeService.cs (Application handlers must not reference Infrastructure).
// Implementation unchanged below.

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