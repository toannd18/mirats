using aspire_react.Server.Application.Dashboard.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// [Giai đoạn 2-cuối] Dashboard migrated to MediatR — 6 GET-only analytics endpoints, all
/// company-scoped verbatim (superuser sees all; regular user own company + floater).
/// Read-only: no Commands, no Behaviors needed, no mutation anywhere.
/// NOTE: monthly-checkout-trend carries PRE-EXISTING BUG-J (superuser → 500, see
/// docs/BACKLOG.md) — reproduced verbatim for parity, fix requires its own approved task.
/// </summary>
[ApiController]
[Route("api/v1/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly IMediator _mediator;
    public DashboardController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("summary")]
    [Authorize]
    public async Task<IActionResult> GetSummary()
    {
        var summary = await _mediator.Send(new GetDashboardSummaryQuery());
        return Ok(new { status = "success", data = summary });
    }

    [HttpGet("recent-activity")]
    [Authorize]
    public async Task<IActionResult> GetRecentActivity()
    {
        var logs = await _mediator.Send(new GetRecentActivityQuery());
        return Ok(new { status = "success", data = logs });
    }

    [HttpGet("assets-by-status")]
    [Authorize]
    public async Task<IActionResult> GetAssetsByStatus()
    {
        var data = await _mediator.Send(new GetAssetsByStatusQuery());
        return Ok(new { status = "success", data });
    }

    [HttpGet("assets-by-category")]
    [Authorize]
    public async Task<IActionResult> GetAssetsByCategory()
    {
        var data = await _mediator.Send(new GetAssetsByCategoryQuery());
        return Ok(new { status = "success", data });
    }

    [HttpGet("low-stock")]
    [Authorize]
    public async Task<IActionResult> GetLowStock([FromQuery] Guid? companyId)
    {
        var data = await _mediator.Send(new GetLowStockQuery(companyId));
        return Ok(new { status = "success", data });
    }

    [HttpGet("monthly-checkout-trend")]
    [Authorize]
    public async Task<IActionResult> GetMonthlyTrend()
    {
        var data = await _mediator.Send(new GetMonthlyCheckoutTrendQuery());
        return Ok(new { status = "success", data });
    }
}
