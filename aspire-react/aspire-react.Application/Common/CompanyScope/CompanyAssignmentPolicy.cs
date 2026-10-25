using aspire_react.Server.Domain.Interfaces;

namespace aspire_react.Server.Application.Common.CompanyScope;

/// <summary>
/// [DF-8] Shared company-assignment rules for Create/Update handlers.
///
/// Create:
/// - superuser: body value wins (including null → floater);
/// - non-superuser with a company: null → own company, same → own, different → forbidden;
/// - companyless non-superuser (the service returns Guid.Empty): legacy value wins; callers that
///   require a real company for a specific entity (e.g. User) must reject explicitly.
///
/// Update (only when CompanyId is explicitly present):
/// - superuser: allowed;
/// - non-superuser: may keep their own company; clearing (Guid.Empty) or changing to any other
///   company is forbidden. Absent field (null) is not a change.
/// </summary>
public static class CompanyAssignmentPolicy
{
    public readonly record struct CreateResult(bool Allowed, Guid? CompanyId);

    public static async Task<CreateResult> ForCreateAsync(
        ICompanyScopeService companyScope, Guid? requestedCompanyId)
    {
        if (companyScope.IsSuperUser())
            return new CreateResult(true, requestedCompanyId);

        var userCompanyId = await companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && userCompanyId.Value != Guid.Empty)
        {
            if (requestedCompanyId.HasValue && requestedCompanyId.Value != userCompanyId.Value)
                return new CreateResult(false, null);
            return new CreateResult(true, userCompanyId.Value);
        }

        // Companyless non-superuser: preserve the legacy body-value behavior. Entity-specific
        // handlers (User) can require a company before or after this rule.
        return new CreateResult(true, requestedCompanyId);
    }

    public static async Task<bool> CanChangeAsync(
        ICompanyScopeService companyScope, Guid? requestedCompanyId)
    {
        // Field absent → no company change.
        if (!requestedCompanyId.HasValue)
            return true;

        if (companyScope.IsSuperUser())
            return true;

        var userCompanyId = await companyScope.GetCurrentUserCompanyIdAsync();
        if (!userCompanyId.HasValue || userCompanyId.Value == Guid.Empty)
            return false;

        // Guid.Empty is the explicit "clear to floater" sentinel.
        if (requestedCompanyId.Value == Guid.Empty)
            return false;

        return requestedCompanyId.Value == userCompanyId.Value;
    }
}
