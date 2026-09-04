using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.MaintenanceTemplates;

/// <summary>
/// Shared lookups + guards for MaintenanceTemplates (moved verbatim from MaintenanceTemplatesController
/// private helpers). Every write/read handler uses the SAME visibility rule: template is visible when
/// the caller is superuser (scope null), the template is a floater (CompanyId null), or it belongs to
/// the caller's company. Out-of-scope → null → caller returns 404 (hide existence).
/// </summary>
internal static class MaintenanceTemplateAccess
{
    /// <summary>Template lookup + visibility check (Include SystemInfo.Positions for [MC-7d] + Company).</summary>
    internal static async Task<MaintenanceChecklistTemplate?> GetVisibleTemplateAsync(
        IApplicationDbContext context, ICompanyScopeService companyScope, Guid id)
    {
        var userCompanyId = await companyScope.GetCurrentUserCompanyIdAsync();
        var t = await context.MaintenanceChecklistTemplates
            .Include(x => x.SystemInfo)
                .ThenInclude(s => s.Positions) // [MC-7d] vị trí hệ thống template cho multi-select
            .Include(x => x.Company)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return null;
        if (userCompanyId.HasValue && t.CompanyId.HasValue && t.CompanyId.Value != userCompanyId.Value)
            return null;
        return t;
    }

    internal static async Task<MaintenanceChecklistTemplateVersion?> GetVersionOfTemplateAsync(
        IApplicationDbContext context, MaintenanceChecklistTemplate template, Guid versionId)
        => await context.MaintenanceChecklistTemplateVersions
            .FirstOrDefaultAsync(v => v.Id == versionId && v.TemplateId == template.Id);

    /// <summary>The IMMUTABLE guard source of truth: any campaign pinning this version.</summary>
    internal static Task<bool> VersionHasCampaignsAsync(IApplicationDbContext context, Guid versionId)
        => context.MaintenanceCampaigns.AsNoTracking().AnyAsync(c => c.TemplateVersionId == versionId);

    /// <summary>Any campaign pinning ANY version of this template (drives FIELD_LOCKED / delete-guard).</summary>
    internal static async Task<bool> TemplateHasCampaignsAsync(IApplicationDbContext context, Guid templateId)
    {
        var versionIds = await context.MaintenanceChecklistTemplateVersions.AsNoTracking()
            .Where(v => v.TemplateId == templateId)
            .Select(v => v.Id)
            .ToListAsync();
        if (versionIds.Count == 0) return false;
        return await context.MaintenanceCampaigns.AsNoTracking()
            .AnyAsync(c => versionIds.Contains(c.TemplateVersionId));
    }

    /// <summary>SystemInfo must exist AND be inside the caller's company scope (floater systems visible to all).</summary>
    internal static async Task<bool> IsSystemVisibleAsync(
        IApplicationDbContext context, ICompanyScopeService companyScope, Guid systemInfoId)
    {
        var userCompanyId = await companyScope.GetCurrentUserCompanyIdAsync();
        return await context.SystemInfos.AsNoTracking().AnyAsync(s =>
            s.Id == systemInfoId &&
            (!userCompanyId.HasValue || s.CompanyId == null || s.CompanyId == userCompanyId.Value));
    }

    /// <summary>timestamp with time zone columns MUST receive Kind=Utc (DateTime Kind convention).</summary>
    internal static DateTime ToUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
