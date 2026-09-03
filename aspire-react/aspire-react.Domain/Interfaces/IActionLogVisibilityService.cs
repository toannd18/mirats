using aspire_react.Server.Domain.Entities;

namespace aspire_react.Server.Domain.Interfaces;

/// <summary>
/// Company-visibility filter for a bounded list of materialized action-logs (Task S1). Extracted from
/// the identical private methods previously duplicated in <c>ReportsController</c> and
/// <c>DashboardController</c> (both had a byte-for-byte copy). Centralizing it means a company-scoping
/// bug fix applies in exactly one place instead of two that could drift apart.
/// [Giai đoạn 2-cuối] Interface moved verbatim from Infrastructure/Services/ActionLogVisibilityService.cs
/// so Application Dashboard queries can consume it without referencing Infrastructure
/// (implementation stays in Infrastructure — same pattern as ICompanyScopeService/IActionLogService).
/// </summary>
public interface IActionLogVisibilityService
{
    /// <summary>
    /// Filters a bounded list of materialized action-logs down to those whose item belongs to the
    /// given user's company (or is company-less / floater). Resolves item companies in batched
    /// queries (one round-trip per item type) to avoid an N+1 per log row.
    /// </summary>
    Task<List<ActionLog>> FilterVisibleLogsAsync(IReadOnlyList<ActionLog> logs, Guid userCompanyId);
}
