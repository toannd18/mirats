using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.AssetMaintenances.Commands;

/// <summary>Assignee validation failure (message + error_code), ported from the controller helper.</summary>
public record AssigneeValidationError(string Message, string ErrorCode);

/// <summary>
/// Shared assignee rules (ported verbatim from AssetMaintenancesController helpers, subtask C).
/// Validate: distinct, max 5, all users must exist, same-company as the record (superuser
/// scope-null and floater Guid.Empty skip). Replace: remove-all + insert distinct users.
/// Used by Update now; Create (subtask B) was refactored onto it as well — single source.
/// </summary>
internal static class MaintenanceAssignees
{
    internal static async Task<AssigneeValidationError?> ValidateAsync(
        IApplicationDbContext context,
        ICompanyScopeService companyScope,
        Guid[]? assigneeUserIds,
        Guid maintenanceCompanyId,
        CancellationToken cancellationToken)
    {
        if (assigneeUserIds == null || assigneeUserIds.Length == 0) return null;

        var distinct = assigneeUserIds.Distinct().ToArray();
        if (distinct.Length > 5)
            return new AssigneeValidationError("Tối đa 5 người phụ trách cho một bản ghi bảo trì.", "MAX_5_ASSIGNEES");

        var users = await context.Users.AsNoTracking()
            .Where(u => distinct.Contains(u.Id))
            .Select(u => new { u.Id, u.CompanyId })
            .ToListAsync(cancellationToken);
        if (users.Count != distinct.Length)
            return new AssigneeValidationError("Có người phụ trách không tồn tại trong hệ thống.", "INVALID_ASSIGNEE");

        // Company isolation (same principle as the user picker in other modules): a regular user may
        // only assign users of the SAME company as the record. Superuser (userCompanyId == null) and
        // floater records (Guid.Empty, manageable by everyone) skip the check.
        var userCompanyId = await companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && maintenanceCompanyId != Guid.Empty
            && users.Any(u => u.CompanyId != maintenanceCompanyId))
            return new AssigneeValidationError("Người phụ trách phải thuộc cùng công ty với bản ghi bảo trì.", "ASSIGNEE_COMPANY_MISMATCH");

        return null;
    }

    internal static async Task ReplaceAsync(
        IApplicationDbContext context,
        Guid maintenanceId,
        Guid[]? assigneeUserIds,
        CancellationToken cancellationToken)
    {
        var existing = await context.AssetMaintenanceAssignees
            .Where(a => a.MaintenanceId == maintenanceId)
            .ToListAsync(cancellationToken);
        context.AssetMaintenanceAssignees.RemoveRange(existing);

        if (assigneeUserIds != null)
        {
            foreach (var uid in assigneeUserIds.Distinct())
            {
                context.AssetMaintenanceAssignees.Add(new AssetMaintenanceAssignee
                {
                    MaintenanceId = maintenanceId,
                    UserId = uid,
                    AssignedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                });
            }
        }
    }
}
