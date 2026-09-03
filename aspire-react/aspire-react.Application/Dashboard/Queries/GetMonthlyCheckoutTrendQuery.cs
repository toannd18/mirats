using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Dashboard.Queries;

public record MonthlyCheckoutTrendItemDto(string Month, int CheckoutCount, int CheckinCount);

/// <summary>
/// [Giai đoạn 2-cuối] GET /api/v1/dashboard/monthly-checkout-trend (extracted verbatim from
/// DashboardController.GetMonthlyTrend — including the PRE-EXISTING BUG-J defect, see
/// docs/BACKLOG.md): for SUPERUSERS, visibleAssetIds stays NULL and the expression
/// `visibleAssetIds.Contains(l.ItemId)` inside the EF query throws ArgumentNullException
/// during translation → raw 500 (CONFIRMED via reproduction on the pre-migration binary —
/// happens deterministically for every superuser). Regular users work fine.
/// The handler reproduces the exact same query structure for parity; fix requires its own
/// approved task (BUG-J).
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
        var visibleAssetIds = userCompanyId == null
            ? null
            : await _context.Assets
                .AsNoTracking()
                .Where(a => a.CompanyId == null || a.CompanyId == userCompanyId.Value)
                .Select(a => a.Id).ToListAsync(cancellationToken);
        var data = await _context.ActionLogs
            .AsNoTracking()
            .Where(l => l.ItemType == ItemType.Asset && l.ActionDate >= twelveMonthsAgo && l.DeletedAt == null
                        && (visibleAssetIds == null || visibleAssetIds.Contains(l.ItemId)))
            .GroupBy(l => new { l.ActionDate.Year, l.ActionDate.Month })
            .Select(g => new
            {
                month = $"{g.Key.Year}-{g.Key.Month:D2}",
                checkoutCount = g.Count(l => l.ActionType == ActionType.Checkout),
                checkinCount = g.Count(l => l.ActionType == ActionType.Checkin)
            })
            .OrderBy(x => x.month)
            .ToListAsync(cancellationToken);

        return data
            .Select(x => new MonthlyCheckoutTrendItemDto(x.month, x.checkoutCount, x.checkinCount))
            .ToList();
    }
}
