namespace aspire_react.Server.Domain.Interfaces;

/// <summary>
/// [Giai đoạn 0.1 — F1] Moved verbatim from Infrastructure/Services/CompanyScopeService.cs so that
/// Application handlers can depend on the contract WITHOUT referencing the Infrastructure project.
/// Implementation (CompanyScopeService) stays in Infrastructure. Content unchanged.
/// </summary>
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
