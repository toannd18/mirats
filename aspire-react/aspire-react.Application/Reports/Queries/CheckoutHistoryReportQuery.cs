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
/// ⚠️ TODO BUG-L (MEDIUM, docs/BACKLOG.md) — VERBATIM DEFECT: with startDate/endDate filters
/// the query compares a Kind=Unspecified DateTime against a timestamptz column → Npgsql throws
/// → raw 500 (CONFIRMED via reproduction on the pre-migration binary). Zero frontend impact
/// (no caller passes date filters — grep-verified). Fix = SpecifyKind/UTC — needs own approval.
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
        var candidates = await _context.ActionLogs
            .Include(l => l.Creator)
            .AsNoTracking()
            .Where(l => l.ActionType == Domain.Enums.ActionType.Checkout || l.ActionType == Domain.Enums.ActionType.Checkin)
            .Where(l => !request.StartDate.HasValue || l.ActionDate >= request.StartDate.Value)
            .Where(l => !request.EndDate.HasValue || l.ActionDate <= request.EndDate.Value)
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
