using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Dashboard.Queries;

public record MonthlyCheckoutTrendItemDto(string Month, int CheckoutCount, int CheckinCount);

/// <summary>
/// [Giai đoạn 2-cuối] GET /api/v1/dashboard/monthly-checkout-trend (extracted verbatim from
/// DashboardController.GetMonthlyTrend).
/// [BUG-J FIX 2026-09-05] Behavior change approved (500 → 200): for SUPERUSERS visibleAssetIds
/// was null and `visibleAssetIds.Contains(l.ItemId)` inside the EF expression threw
/// ArgumentNullException during translation → raw 500 (CONFIRMED via reproduction on the
/// pre-migration binary). Fix = branch the query on the scope INSTEAD of evaluating Contains on
/// a null collection: superuser → NO asset-id filter (sees every Asset log); regular user →
/// contains-filter as before. Same null-conditional pattern used by every other dashboard query
/// in this section.
/// </summary>
public record GetMonthlyCheckoutTrendQuery : IRequest<IReadOnlyList<MonthlyCheckoutTrendItemDto>>;

public class GetMonthlyCheckoutTrendQueryHandler : IRequestHandler<GetMonthlyCheckoutTrendQuery, IReadOnlyList<MonthlyCheckoutTrendItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetMonthlyCheckoutTrendQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<IReadOnlyList<MonthlyCheckoutTrendItemDto>> Handle(GetMonthlyCheckoutTrendQuery request, CancellationToken cancellationToken)
    {
        var twelveMonthsAgo = DateTime.UtcNow.AddMonths(-12);
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();

        // [BUG-J FIX — RE-DIAGNOSED 2026-09-05] The REAL root cause (verified on live Postgres after
        // the scope-branch alone still 500-ed): the inline projection `$"{g.Key.Year}-{g.Key.Month:D2}"`
        // is NOT translatable by Npgsql (the ":D2" format specifier) — EF throws during translation
        // for EVERY caller (superuser AND regular; InMemory unit tests pass because they evaluate the
        // projection client-side). The old diagnosis (visibleAssetIds null → ArgumentNullException at
        // Contains) was an inference error. Fix: group by (Year, Month) in SQL, aggregate the counts
        // in SQL, then build the month STRING client-side (D2 formatting in memory).
        var query = _context.ActionLogs
            .AsNoTracking()
            .Where(l => l.ItemType == ItemType.Asset && l.ActionDate >= twelveMonthsAgo && l.DeletedAt == null);
        if (userCompanyId != null)
        {
            // Regular user: restrict to visible assets (own company + floaters). Superuser: no filter.
            var visibleAssetIds = await _context.Assets
                .AsNoTracking()
                .Where(a => a.CompanyId == null || a.CompanyId == userCompanyId.Value)
                .Select(a => a.Id).ToListAsync(cancellationToken);
            query = query.Where(l => visibleAssetIds.Contains(l.ItemId));
        }

        var data = await query
            .GroupBy(l => new { l.ActionDate.Year, l.ActionDate.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                checkoutCount = g.Count(l => l.ActionType == ActionType.Checkout),
                checkinCount = g.Count(l => l.ActionType == ActionType.Checkin)
            })
            .OrderBy(x => x.Year).OrderBy(x => x.Month)
            .ToListAsync(cancellationToken);

        return data
            .Select(x => new MonthlyCheckoutTrendItemDto($"{x.Year}-{x.Month:D2}", x.checkoutCount, x.checkinCount))
            .ToList();
    }
}
