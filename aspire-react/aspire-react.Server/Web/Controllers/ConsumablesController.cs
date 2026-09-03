using System.Security.Claims;
using aspire_react.Server.Application.Consumables.Commands;
using aspire_react.Server.Application.Consumables.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// [Giai đoạn 3 — Nặng] Consumables migrated to MediatR — 9 endpoints. Log style VERBATIM:
/// IActionLogService.LogAction (named-args + LogMeta) in the same SaveChanges as data (NOT
/// ILoggableCommand — LogAction's enrichment path is the pre-migration behavior). Checkout
/// owns the transaction boundary verbatim (RunTransactional moved into the command handler).
/// Error mapping verbatim: NOT_FOUND → 404; CONFIRMED_CONSUMABLE_LOCKED / FIELD_LOCKED /
/// COMPANY_MISMATCH / CONSUMABLE_HAS_CHECKOUTS → 400 WITH error_code; "Vật tư đã được xác nhận."
/// → 400 WITHOUT error_code.
/// </summary>
[ApiController]
[Route("api/v1/consumables")]
public class ConsumablesController : ControllerBase
{
    private readonly IMediator _mediator;
    public ConsumablesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    private Guid GetCurrentUserId()
    {
        // [SEC-FIX CLAIM-CLEANUP, 2026-08-23] ONLY "local_user_id" (JIT-stamped) is a user identity
        // source — Keycloak sub/preferred_username are never used (bug-class 1). Absent → Guid.Empty.
        if (Guid.TryParse(User.FindFirstValue("local_user_id"), out var local)) return local;
        return Guid.Empty;
    }

    [HttpGet]
    [Authorize(Policy = "consumables.view")]
    public async Task<IActionResult> GetConsumables([FromQuery] string? search, [FromQuery] Guid? categoryId,
        [FromQuery] Guid? locationId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _mediator.Send(new ListConsumablesQuery(search, categoryId, locationId, page, pageSize));
        return Ok(new { status = "success", data = result.Items, pagination = new { page, pageSize, totalItems = result.Total, totalPages = (int)Math.Ceiling((double)result.Total / pageSize), hasNextPage = page * pageSize < result.Total, hasPreviousPage = page > 1 } });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "consumables.view")]
    public async Task<IActionResult> GetConsumable(Guid id)
    {
        var c = await _mediator.Send(new GetConsumableByIdQuery(id));
        if (c == null) return NotFound(new { status = "error", message = "Consumable not found." });
        return Ok(new { status = "success", data = c });
    }

    [HttpPost]
    [Authorize(Policy = "consumables.create")]
    public async Task<IActionResult> Create([FromBody] CreateConsumableRequest r)
    {
        var result = await _mediator.Send(new CreateConsumableCommand(
            r.Name, r.ItemNo, r.Qty, r.MinAmt, r.CategoryId, r.ManufacturerId, r.SupplierId,
            r.LocationId, r.CompanyId, r.ModelNumber, r.OrderNumber, r.PurchaseCost,
            r.PurchaseDate, r.Notes, r.Image, GetCurrentUserId()));
        if (!result.Success) return MapFailure(result);
        return CreatedAtAction(nameof(GetConsumable), new { id = result.Id }, new { status = "success", message = result.Message, data = new { result.Id, result.Name } });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "consumables.edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateConsumableRequest r)
    {
        var result = await _mediator.Send(new UpdateConsumableCommand(
            id, r.Name, r.ItemNo, r.Qty, r.MinAmt, r.CategoryId, r.ManufacturerId, r.SupplierId,
            r.LocationId, r.CompanyId, r.ModelNumber, r.OrderNumber, r.PurchaseCost, r.PurchaseDate,
            r.Notes, r.Image, GetCurrentUserId()));
        if (!result.Success) return MapFailure(result);
        return Ok(new { status = "success", message = "Consumable updated." });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "consumables.delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await _mediator.Send(new DeleteConsumableCommand(id, GetCurrentUserId()));
        if (!result.Success) return MapFailure(result);
        return Ok(new { status = "success", message = "Consumable deleted." });
    }

    [HttpPut("{id:guid}/confirm")]
    [Authorize(Policy = "consumables.edit")]
    public async Task<IActionResult> Confirm(Guid id)
    {
        var result = await _mediator.Send(new ConfirmConsumableCommand(id, GetCurrentUserId()));
        if (!result.Success) return MapFailure(result);
        return Ok(new { status = "success", message = "Consumable confirmed." });
    }

    [HttpPost("{id:guid}/checkout")]
    [Authorize(Policy = "consumables.checkout")]
    public async Task<IActionResult> Checkout(Guid id, [FromBody] CheckoutConsumableRequest r)
    {
        var result = await _mediator.Send(new CheckoutConsumableCommand(id, r.UserId, r.Quantity, r.Note, GetCurrentUserId()));
        if (!result.Success) return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        return Ok(new { status = "success", message = result.Message });
    }

    [HttpGet("{id:guid}/checkouts")]
    [Authorize(Policy = "consumables.view")]
    public async Task<IActionResult> GetCheckouts(Guid id)
    {
        var checkouts = await _mediator.Send(new GetConsumableCheckoutsQuery(id));
        if (checkouts == null) return NotFound(new { status = "error", message = "Consumable not found." });
        return Ok(new { status = "success", data = checkouts });
    }

    [HttpGet("low-stock")]
    [Authorize(Policy = "consumables.view")]
    public async Task<IActionResult> GetLowStock()
    {
        var items = await _mediator.Send(new GetConsumableLowStockQuery());
        return Ok(new { status = "success", data = items });
    }

    private IActionResult MapFailure(ConsumableResult result)
    {
        if (result.ErrorCode == "NOT_FOUND")
            return NotFound(new { status = "error", message = result.Message });

        object body = result.ErrorCode is null
            ? new { status = "error", message = result.Message }
            : new { status = "error", message = result.Message, error_code = result.ErrorCode };
        return BadRequest(body);
    }
}

/// <summary>Request DTOs — verbatim from the pre-migration records.</summary>
public record CreateConsumableRequest(
    string Name, string? ItemNo, int Qty, int MinAmt,
    Guid? CategoryId, Guid? ManufacturerId, Guid? SupplierId,
    Guid? LocationId, Guid? CompanyId,
    string? ModelNumber, string? OrderNumber,
    decimal? PurchaseCost, DateTime? PurchaseDate, string? Notes, string? Image);

/// <summary>
/// Patch-style Update DTO (Task M1): every field is nullable so a partial payload only changes the
/// fields explicitly sent, without wiping the others back to null/0. Distinct from the Create DTO
/// (whose Qty/MinAmt/Name are required) — the two intents must not share one non-nullable DTO.
/// </summary>
public record UpdateConsumableRequest(
    string? Name = null, string? ItemNo = null, int? Qty = null, int? MinAmt = null,
    Guid? CategoryId = null, Guid? ManufacturerId = null, Guid? SupplierId = null,
    Guid? LocationId = null, Guid? CompanyId = null,
    string? ModelNumber = null, string? OrderNumber = null,
    decimal? PurchaseCost = null, DateTime? PurchaseDate = null, string? Notes = null, string? Image = null);

public record CheckoutConsumableRequest(Guid? UserId, int Quantity, string? Note);
