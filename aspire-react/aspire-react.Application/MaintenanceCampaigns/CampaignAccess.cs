using System.Text.Json;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.MaintenanceCampaigns;

/// <summary>
/// Shared lookups/guards/logging for MaintenanceCampaigns (moved verbatim from
/// MaintenanceCampaignsController privates). Manual typed-log path (IActionLogService.Log) — same
/// rationale as MaintenanceTemplates: LogCampaignAction uses TargetSystemInfoId/Name snapshot which
/// the ActionLogBehavior's LogAction path cannot express; Create owns its own BUG-A transaction
/// (ILoggableCommand would nested-throw).
/// </summary>
internal static class CampaignAccess
{
    /// <summary>Campaign lookup + company visibility (floater/own-company; superuser sees all).</summary>
    internal static async Task<MaintenanceCampaign?> GetVisibleCampaignAsync(
        IApplicationDbContext context, ICompanyScopeService companyScope, Guid id)
    {
        var userCompanyId = await companyScope.GetCurrentUserCompanyIdAsync();
        var c = await context.MaintenanceCampaigns
            .Include(x => x.SystemInfo)
            .Include(x => x.TemplateVersion)
            .Include(x => x.DeviceSnapshots)
            .Include(x => x.Results)
            .Include(x => x.Executors).ThenInclude(e => e.User)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return null;
        if (userCompanyId.HasValue && c.CompanyId.HasValue && c.CompanyId.Value != userCompanyId.Value)
            return null;
        return c;
    }

    /// <summary>Typed log staging — verbatim port of LogCampaignAction (default JSON options, TargetSystemInfo snapshot).</summary>
    internal static ActionLogEntry BuildLog(
        ActionType actionType,
        MaintenanceCampaign campaign,
        Guid currentUserId,
        string note,
        object? meta = null)
    {
        return new ActionLogEntry
        {
            ItemType = ItemType.MaintenanceCampaign,
            ItemId = campaign.Id,
            ActionType = actionType,
            CreatedBy = currentUserId,
            CompanyId = campaign.CompanyId,
            TargetSystemInfoId = campaign.SystemInfoId,
            TargetSystemInfoName = campaign.SystemInfo?.Name,
            LogMeta = meta == null ? null : JsonSerializer.Serialize(meta),
            Note = note
        };
    }

    /// <summary>Resolves the template + current published version to pin, or a typed why-not (error code + message verbatim).</summary>
    internal static async Task<(MaintenanceChecklistTemplate? template, MaintenanceChecklistTemplateVersion? version, string? errorCode, string? errorMessage)>
        ResolvePinableVersionAsync(IApplicationDbContext context, ICompanyScopeService companyScope, Guid systemInfoId, Guid? templateId)
    {
        var userCompanyId = await companyScope.GetCurrentUserCompanyIdAsync();

        var template = await context.MaintenanceChecklistTemplates
            .Include(t => t.Versions)
            .FirstOrDefaultAsync(t => t.Id == templateId.GetValueOrDefault());

        if (templateId.HasValue)
        {
            if (template == null)
                return (null, null, "NOT_FOUND", "Template not found.");
            if (userCompanyId.HasValue && template.CompanyId.HasValue && template.CompanyId.Value != userCompanyId.Value)
                return (null, null, "NOT_FOUND", "Template not found.");
            if (template.SystemInfoId != systemInfoId)
                return (null, null, "TEMPLATE_SYSTEM_MISMATCH", "Template không thuộc hệ thống đã chọn.");
        }
        else
        {
            var templates = await context.MaintenanceChecklistTemplates.AsNoTracking()
                .Where(t => t.SystemInfoId == systemInfoId &&
                            (!userCompanyId.HasValue || t.CompanyId == null || t.CompanyId == userCompanyId.Value))
                .ToListAsync();
            if (templates.Count == 0)
                return (null, null, "NO_TEMPLATE", "Hệ thống chưa có template bảo dưỡng.");
            if (templates.Count > 1)
                return (null, null, "AMBIGUOUS_TEMPLATE", "Hệ thống có nhiều template — cần chỉ định templateId.");
            template = await context.MaintenanceChecklistTemplates
                .Include(t => t.Versions)
                .FirstAsync(t => t.Id == templates[0].Id);
        }

        var current = template!.Versions.FirstOrDefault(v => v.IsCurrent);
        if (current == null || !current.PublishedAt.HasValue)
            return (null, null, "NO_CURRENT_VERSION", "Template chưa có version hiện hành đã publish — hãy publish trước.");

        return (template, current, null, null);
    }

    /// <summary>
    /// [MC-7c] Cặp (item, snapshot) có thuộc phạm vi áp dụng không: item KHÔNG khai báo vị trí
    /// (universal) → áp dụng mọi snapshot; item có khai báo → snapshot.SystemPositionId phải ∈ danh sách.
    /// </summary>
    internal static async Task<bool> IsApplicablePairAsync(
        IApplicationDbContext context, Guid itemId, Guid? snapshotSystemPositionId)
    {
        var declared = await context.MaintenanceChecklistItemPositions.AsNoTracking()
            .Where(ip => ip.ItemId == itemId)
            .Select(ip => ip.SystemPositionId)
            .ToListAsync();
        if (declared.Count == 0) return true; // universal
        return snapshotSystemPositionId.HasValue && declared.Contains(snapshotSystemPositionId.Value);
    }

    /// <summary>timestamp with time zone columns MUST receive Kind=Utc (DateTime Kind convention).</summary>
    internal static DateTime ToUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
