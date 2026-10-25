using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Systems.Commands;
using aspire_react.Server.Application.Systems.Queries;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// [Giai đoạn 3] Systems migrated to MediatR — system-scoped read aggregations for the
/// SystemDetailPage. Routes unchanged: /api/v1/systems...
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

    /// <summary>
    /// [FEAT-SPARE 3] Items currently held by this system (net issued &gt; 0). There is no per-system
    /// quota: spare parts are shared inventory and the returned remaining is the shared stock.
    /// </summary>
    [HttpGet("{id:guid}/spare-parts")]
    [Authorize(Policy = "systems.view")]
    public async Task<IActionResult> GetSpareParts(Guid id)
    {
        var items = await _mediator.Send(new GetSystemSparePartsQuery(id));
        if (items == null)
            return NotFound(new { status = "error", message = "Hệ thống không tồn tại hoặc ngoài phạm vi công ty của bạn." });

        return Ok(new { status = "success", data = items });
    }

    [HttpPost("{id:guid}/spare-parts/issue")]
    public Task<IActionResult> IssueSparePart(Guid id, [FromBody] SparePartMovementRequest body,
        [FromServices] IAuthorizationService authorizationService,
        [FromServices] ICurrentUserService currentUserService)
        => RecordSparePartMovement(id, body, SparePartMovementDirection.Issue,
            authorizationService, currentUserService);

    [HttpPost("{id:guid}/spare-parts/return")]
    public Task<IActionResult> ReturnSparePart(Guid id, [FromBody] SparePartMovementRequest body,
        [FromServices] IAuthorizationService authorizationService,
        [FromServices] ICurrentUserService currentUserService)
        => RecordSparePartMovement(id, body, SparePartMovementDirection.Return,
            authorizationService, currentUserService);

    private async Task<IActionResult> RecordSparePartMovement(Guid id, SparePartMovementRequest body,
        SparePartMovementDirection direction, IAuthorizationService authorizationService,
        ICurrentUserService currentUserService)
    {
        if ((body.ComponentId.HasValue ? 1 : 0) + (body.ConsumableId.HasValue ? 1 : 0) != 1)
            return BadRequest(new
            {
                status = "error",
                message = "Phải chọn đúng một linh kiện hoặc vật tư.",
                error_code = "INVALID_SPARE_ITEM",
            });

        var policy = body.ComponentId.HasValue ? "components.checkout" : "consumables.checkout";
        var authorization = await authorizationService.AuthorizeAsync(User, policy);
        if (!authorization.Succeeded)
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                status = "error",
                message = "Bạn không có quyền cấp phát vật tư/linh kiện cho hệ thống.",
                error_code = "FORBIDDEN",
            });

        var result = await _mediator.Send(new RecordSystemSparePartMovementCommand(
            id, body.ComponentId, body.ConsumableId, body.Quantity, direction, body.Note,
            currentUserService.GetLocalUserId()));
        if (!result.Success)
        {
            if (result.ErrorCode == "NOT_FOUND")
                return NotFound(new { status = "error", message = result.Message, error_code = result.ErrorCode });
            if (result.ErrorCode == "SPARE_PART_INSUFFICIENT_STOCK")
                return Conflict(new { status = "error", message = result.Message, error_code = result.ErrorCode });
            return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        }

        return Ok(new
        {
            status = "success",
            data = new
            {
                movementId = result.MovementId,
                remaining = result.Remaining,
                issuedToSystem = result.IssuedToSystem,
            },
        });
    }

    public sealed record SparePartMovementRequest(
        Guid? ComponentId, Guid? ConsumableId, int Quantity, string? Note);
}
