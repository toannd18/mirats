using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Users.Commands;

/// <summary>
/// [FIX-N5 (phần còn lại) 2026-10-02] Scope validation for the department/location references a
/// user record can carry.
///
/// WHY: CompanyId was scope-checked (N5) but the two other company-owned references were NOT —
/// verified live: an admin of company A could attach a user to a department/location of company B
/// (cross-tenant reference; same bug class as BUG-G, just on a different field). Rules mirror the
/// rest of the project:
///   * the referenced id must EXIST → 400 <c>RESOURCE_NOT_FOUND</c> (BUG-H precedent; previously a
///     raw FK violation surfaced as 500);
///   * a reference pointing at a REAL company must be inside the ACTOR's scope — superuser any,
///     regular user own company or its subtree (<see cref="ICompanyScopeService.IsCompanyIdInUserScopeAsync"/>);
///     a company-less (floater, CompanyId == null) department/location is always allowed
///     → 400 <c>COMPANY_MISMATCH</c> otherwise.
/// Only fields that were actually SENT are validated (patch semantics: absent → keep), and the
/// <c>Guid.Empty</c> clear-sentinel is skipped entirely — it means "remove the reference"
/// (handled by the caller), not "point at id 00000000-…", so it must not be looked up.
/// </summary>
internal static class UserReferenceScope
{
    public static async Task<(string? ErrorCode, string? Message)> ValidateAsync(
        IApplicationDbContext context,
        ICompanyScopeService companyScope,
        Guid? departmentId,
        Guid? locationId,
        CancellationToken cancellationToken)
    {
        if (departmentId.HasValue && departmentId.Value != Guid.Empty)
        {
            var dept = await context.Departments.AsNoTracking()
                .Where(d => d.Id == departmentId.Value)
                .Select(d => new { d.CompanyId })
                .FirstOrDefaultAsync(cancellationToken);

            if (dept is null)
                return ("RESOURCE_NOT_FOUND", "Trường tham chiếu không tồn tại: departmentId.");
            if (dept.CompanyId.HasValue && !await companyScope.IsCompanyIdInUserScopeAsync(dept.CompanyId.Value))
                return ("COMPANY_MISMATCH", "Bạn chỉ được gán người dùng vào phòng ban thuộc công ty của mình.");
        }

        if (locationId.HasValue && locationId.Value != Guid.Empty)
        {
            var loc = await context.Locations.AsNoTracking()
                .Where(l => l.Id == locationId.Value)
                .Select(l => new { l.CompanyId })
                .FirstOrDefaultAsync(cancellationToken);

            if (loc is null)
                return ("RESOURCE_NOT_FOUND", "Trường tham chiếu không tồn tại: locationId.");
            if (loc.CompanyId.HasValue && !await companyScope.IsCompanyIdInUserScopeAsync(loc.CompanyId.Value))
                return ("COMPANY_MISMATCH", "Bạn chỉ được gán người dùng vào địa điểm thuộc công ty của mình.");
        }

        return (null, null);
    }
}
