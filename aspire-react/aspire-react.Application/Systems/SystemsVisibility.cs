using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Systems.Queries;

/// <summary>
/// [Giai đoạn 3] Shared system-visibility check for the /systems aggregation endpoints —
/// moved VERBATIM from SystemsController.IsSystemVisibleAsync (both endpoints used the same
/// private method; an internal Application helper keeps that single definition without forcing
/// it onto the IActionLogVisibilityService list-filter contract).
/// DELIBERATE CONVENTION — 404 (NOT 403) for out-of-scope systems: the existence of a system
/// (its code + name) is company-sensitive, so it is hidden entirely from users of other
/// companies. This matches SystemInfoController.Get and ActionLogsController.GetBySystem.
/// Single maintenance records (AssetMaintenancesController) intentionally use 403 instead —
/// do NOT unify the two status codes.
/// </summary>
public static class SystemsVisibility
{
    public static async Task<bool> IsSystemVisibleAsync(
        IApplicationDbContext context, ICompanyScopeService companyScope, Guid systemId, CancellationToken ct = default)
    {
        var userCompanyIds = await companyScope.GetUserCompanyIdsAsync();
        return await context.SystemInfos.AsNoTracking().AnyAsync(s =>
            s.Id == systemId &&
            (companyScope.IsSuperUser() || userCompanyIds.Count == 0 ||
             s.CompanyId == null || userCompanyIds.Contains(s.CompanyId.Value)), ct);
    }
}
