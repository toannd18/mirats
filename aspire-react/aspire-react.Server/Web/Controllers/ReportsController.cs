using aspire_react.Server.Application.Reports.Queries;
using aspire_react.Server.Application.Reports.RepairReports;
using aspire_react.Server.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// [Giai đoạn 3] Reports migrated to MediatR — 4 GET-only report endpoints (no markers).
/// Routes/policies unchanged: /api/v1/reports... (reports.view on all).
/// ⚠️ BUG-L kept verbatim (checkout-history + date filters → 500; zero frontend impact).
/// </summary>
[ApiController]
[Route("api/v1/reports")]
public class ReportsController : ControllerBase
{
    private readonly IMediator _mediator;
    public ReportsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("custom")]
    [Authorize(Policy = "reports.view")]
    public async Task<IActionResult> CustomReport(
        [FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate,
        [FromQuery] Guid? categoryId, [FromQuery] Guid? locationId,
        [FromQuery] AssetStatus? status, [FromQuery] string? groupBy)
    {
        var result = await _mediator.Send(new CustomReportQuery(startDate, endDate, categoryId, locationId, status));
        return Ok(new { status = "success", data = result.Items, total = result.Total });
    }

    [HttpGet("repairs")]
    [Authorize(Policy = "reports.view")]
    public async Task<IActionResult> Repairs(
        [FromQuery] string? period = "month",
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] int? quarter = null,
        [FromQuery] Guid? systemId = null,
        [FromQuery] bool unassignedOnly = false,
        [FromQuery] string? typeGroup = "repair_incident",
        [FromQuery] Guid? categoryId = null,
        [FromQuery] Guid? modelId = null)
    {
        var result = await _mediator.Send(BuildRepairQuery(
            period, year, month, quarter, systemId, unassignedOnly, typeGroup, categoryId, modelId));
        return Ok(new { status = "success", data = result });
    }

    private static RepairReportQuery BuildRepairQuery(
        string? period, int? year, int? month, int? quarter,
        Guid? systemId, bool unassignedOnly, string? typeGroup,
        Guid? categoryId, Guid? modelId) =>
        new(period ?? "month", year, month, quarter, systemId, unassignedOnly,
            typeGroup ?? "repair_incident", categoryId, modelId);

    [HttpGet("depreciation")]
    [Authorize(Policy = "reports.view")]
    public async Task<IActionResult> DepreciationReport()
    {
        var data = await _mediator.Send(new DepreciationReportQuery());
        return Ok(new { status = "success", data });
    }

    [HttpGet("audit")]
    [Authorize(Policy = "reports.view")]
    public async Task<IActionResult> AuditReport()
    {
        var dto = await _mediator.Send(new AuditReportQuery());
        return Ok(new { status = "success", data = new { dto.TotalAudited, dto.NotAudited, dto.OverdueAudit } });
    }

    [HttpGet("checkout-history")]
    [Authorize(Policy = "reports.view")]
    public async Task<IActionResult> CheckoutHistory(
        [FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var result = await _mediator.Send(new CheckoutHistoryReportQuery(startDate, endDate));
        return Ok(new { status = "success", data = result.Items });
    }
}
