using aspire_react.Server.Application.Systems.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// [Giai đoạn 3] Systems migrated to MediatR — system-scoped read aggregations for the
/// SystemDetailPage (2 GET-only endpoints, no markers). Routes unchanged: /api/v1/systems...
/// SystemsVisibility helper moved verbatim (DELIBERATE 404-not-403 convention — see its doc).
/// The assets pagination envelope is built here verbatim from the query's (items, total).
/// </summary>
[ApiController, Route("api/v1/systems"), Authorize]
public class SystemsController : ControllerBase
{
    private readonly IMediator _mediator;
    public SystemsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Assets currently installed in the system. An Asset links to a SystemPosition (child); the
    /// parent SystemInfo is implied — so this aggregates across every child position of the system.
    /// Pass systemPositionId to narrow to a single position (used by the position quick-filter).
    /// </summary>
    [HttpGet("{id:guid}/assets")]
    [Authorize(Policy = "assets.view")]
    public async Task<IActionResult> GetAssets(Guid id, [FromQuery] Guid? systemPositionId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _mediator.Send(new GetSystemAssetsQuery(id, systemPositionId, page, pageSize));
        if (result == null)
            return NotFound(new { status = "error", message = "Hệ thống không tồn tại hoặc ngoài phạm vi công ty của bạn." });

        return Ok(new
        {
            status = "success",
            data = result.Items,
            pagination = new
            {
                page,
                pageSize,
                totalItems = result.Total,
                totalPages = (int)Math.Ceiling((double)result.Total / pageSize),
                hasNextPage = page * pageSize < result.Total,
                hasPreviousPage = page > 1
            }
        });
    }

    /// <summary>
    /// Accessories checked out to the system — aggregate across every child SystemPosition
    /// (AccessoryCheckout.CheckoutType = SystemPosition, TargetId = SystemPosition.Id).
    /// Pass systemPositionId to narrow to a single position.
    /// </summary>
    [HttpGet("{id:guid}/accessories")]
    [Authorize(Policy = "accessories.view")]
    public async Task<IActionResult> GetAccessories(Guid id, [FromQuery] Guid? systemPositionId = null)
    {
        var items = await _mediator.Send(new GetSystemAccessoriesQuery(id, systemPositionId));
        if (items == null)
            return NotFound(new { status = "error", message = "Hệ thống không tồn tại hoặc ngoài phạm vi công ty của bạn." });

        return Ok(new { status = "success", data = items });
    }
}
