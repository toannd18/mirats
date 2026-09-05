using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Reports.Queries;

public record CheckoutHistoryRowDto(
    Guid Id,
    ItemType ItemType,
    Guid ItemId,
    ActionType ActionType,
    string? Note,
    DateTime ActionDate,
    CheckoutHistoryCreatorDto Creator);

public record CheckoutHistoryCreatorDto(Guid Id, string Username, string FirstName, string LastName);

/// <summary>
/// [Giai đoạn 3] GET /api/v1/reports/checkout-history (extracted from
/// ReportsController.CheckoutHistory). Take(200) candidates → IActionLogVisibilityService
/// list-filter (regular users; superuser unfiltered — reused from the Domain interface move).
/// [BUG-L FIX 2026-09-05] Behavior change approved (500 → 200): query-param DateTimes bind as
/// Kind=Unspecified and comparing them directly against the `timestamptz` ActionDate column made
/// Npgsql throw → raw 500 whenever startDate/endDate was supplied (CONFIRMED via reproduction on
/// the pre-migration binary). Fix per the project DateTime Kind convention (workflow doc /
/// HANDOFF_DATETIME_KIND_AUDIT): `timestamp with time zone` comparisons MUST receive Kind=Utc —
/// both filters are normalized with DateTime.SpecifyKind(value, DateTimeKind.Utc) before the
/// comparison. Zero frontend impact (no caller passes date filters — grep-verified); the fix
/// only unblocks future API callers.
/// </summary>
public record CheckoutHistoryReportQuery(DateTime? StartDate, DateTime? EndDate) : IRequest<CheckoutHistoryResult>;

public record CheckoutHistoryResult(IReadOnlyList<CheckoutHistoryRowDto> Items);

public class CheckoutHistoryReportQueryHandler : IRequestHandler<CheckoutHistoryReportQuery, CheckoutHistoryResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogVisibilityService _actionLogVisibility;

    public CheckoutHistoryReportQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogVisibilityService actionLogVisibility)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogVisibility = actionLogVisibility;
    }

    public async Task<CheckoutHistoryResult> Handle(CheckoutHistoryReportQuery request, CancellationToken cancellationToken)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();

        // [BUG-L FIX] Normalize filter Kinds before the timestamptz comparison (DateTime Kind
        // convention): query params arrive Kind=Unspecified → Npgsql throws. Reinterpreted AS UTC
        // (the API contract treats filter instants as UTC — matching the ActionDate values written
        // with DateTime.UtcNow).
        var startDate = request.StartDate.HasValue ? (DateTime?)DateTime.SpecifyKind(request.StartDate.Value, DateTimeKind.Utc) : null;
        var endDate = request.EndDate.HasValue ? (DateTime?)DateTime.SpecifyKind(request.EndDate.Value, DateTimeKind.Utc) : null;

        var candidates = await _context.ActionLogs
            .Include(l => l.Creator)
            .AsNoTracking()
            .Where(l => l.ActionType == Domain.Enums.ActionType.Checkout || l.ActionType == Domain.Enums.ActionType.Checkin)
            .Where(l => !startDate.HasValue || l.ActionDate >= startDate.Value)
            .Where(l => !endDate.HasValue || l.ActionDate <= endDate.Value)
            .OrderByDescending(l => l.ActionDate)
            .Take(200)
            .ToListAsync(cancellationToken);

        // Company scoping: a regular user may only see checkout/checkin history of items in their company.
        var visible = userCompanyId == null
            ? candidates
            : await _actionLogVisibility.FilterVisibleLogsAsync(candidates, userCompanyId.Value);

        var logs = visible
            .Select(l => new CheckoutHistoryRowDto(
                l.Id,
                l.ItemType,
                l.ItemId,
                l.ActionType,
                l.Note,
                l.ActionDate,
                l.Creator == null ? null! : new CheckoutHistoryCreatorDto(l.Creator.Id, l.Creator.Username, l.Creator.FirstName, l.Creator.LastName)))
            .ToList();

        return new CheckoutHistoryResult(logs);
    }
}
