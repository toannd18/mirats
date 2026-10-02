using System.Security.Claims;
using aspire_react.Server.Application.Accessories.Commands;
using aspire_react.Server.Application.Accessories.Queries;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

[ApiController]
[Route("api/v1/accessories")]
/// <summary>
/// [FIX C / N2 remainder 2026-10-02] THIN 100% — this controller used to be the last one running EF
/// queries directly (List/GetById/Update/GetCheckouts, and Update wrote no ActionLog). All four are
/// now Queries/Commands under Application/Accessories, so the constructor only needs IMediator (plus
/// ICurrentUserService for the `local_user_id` claim). N2 covered List/GetById/Update; item C moved
/// the remaining GetCheckouts history endpoint.
/// </summary>
public class AccessoriesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUserService;

    public AccessoriesController(IMediator mediator, ICurrentUserService currentUserService)
    {
        _mediator = mediator;
        _currentUserService = currentUserService;
    }

    // ==================== LIST ====================
    // [FIX-N2 2026-10-02] Moved to ListAccessoriesQuery — verbatim logic (filters, company scope,
    // remaining/low-stock math, pagination shape); the controller is now a thin Send() map.

    [HttpGet]
    [Authorize(Policy = "accessories.view")]
    public async Task<IActionResult> GetAccessories([FromQuery] string? search, [FromQuery] Guid? categoryId,
        [FromQuery] Guid? locationId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _mediator.Send(new ListAccessoriesQuery(search, categoryId, locationId, page, pageSize));

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

    // ==================== GET BY ID ====================
    // [FIX-N2 2026-10-02] Moved to GetAccessoryByIdQuery (same Includes + scoped 404 + projection).

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "accessories.view")]
    public async Task<IActionResult> GetAccessory(Guid id)
    {
        var result = await _mediator.Send(new GetAccessoryByIdQuery(id));

        if (result.Accessory is null)
            return NotFound(new { status = "error", message = "Accessory not found." });

        return Ok(new { status = "success", data = result.Accessory });
    }

    // ==================== CREATE (via CQRS Command) ====================

    [HttpPost]
    [Authorize(Policy = "accessories.create")]
    public async Task<IActionResult> Create([FromBody] CreateAccessoryRequest r)
    {
        var currentUserId = GetCurrentUserId();

        var result = await _mediator.Send(new CreateAccessoryCommand
        {
            Name = r.Name,
            ItemNo = r.ItemNo,
            Qty = r.Qty,
            MinAmt = r.MinAmt,
            CategoryId = r.CategoryId,
            ManufacturerId = r.ManufacturerId,
            SupplierId = r.SupplierId,
            LocationId = r.LocationId,
            CompanyId = r.CompanyId,
            ModelNumber = r.ModelNumber,
            OrderNumber = r.OrderNumber,
            PurchaseCost = r.PurchaseCost,
            PurchaseDate = r.PurchaseDate,
            Notes = r.Notes,
            Image = r.Image,
            CurrentUserId = currentUserId
        });

        if (!result.Success)
            return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });

        return CreatedAtAction(nameof(GetAccessory), new { id = result.AccessoryId },
            new { status = "success", message = result.Message, data = new { Id = result.AccessoryId, Name = r.Name } });
    }

    // ==================== UPDATE ====================
    // [FIX-N2 2026-10-02] Moved to UpdateAccessoryCommand (ILoggableCommand) — the action had NO
    // ActionLog at all before; ActionLogBehavior now persists it in the same transaction as the
    // data change. Guards/order/bodies are verbatim (404 hide-existence → FIELD_LOCKED after
    // checkout history → 400 without error_code while items are checked out → M2 patch assigns).

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "accessories.edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAccessoryRequest r)
    {
        var result = await _mediator.Send(new UpdateAccessoryCommand
        {
            Id = id,
            Name = r.Name,
            ItemNo = r.ItemNo,
            Qty = r.Qty,
            MinAmt = r.MinAmt,
            CategoryId = r.CategoryId,
            ManufacturerId = r.ManufacturerId,
            SupplierId = r.SupplierId,
            LocationId = r.LocationId,
            CompanyId = r.CompanyId,
            ModelNumber = r.ModelNumber,
            OrderNumber = r.OrderNumber,
            PurchaseCost = r.PurchaseCost,
            PurchaseDate = r.PurchaseDate,
            Notes = r.Notes,
            Image = r.Image,
            CurrentUserId = GetCurrentUserId()
        });

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                "NOT_FOUND" => NotFound(new { status = "error", message = result.Message }),
                // No error_code (active checkouts) — verbatim body of the pre-migration action.
                null => BadRequest(new { status = "error", message = result.Message }),
                _ => BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode })
            };
        }

        return Ok(new { status = "success", message = result.Message });
    }

    // ==================== DELETE (via CQRS Command) ====================

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "accessories.delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var currentUserId = GetCurrentUserId();

        var result = await _mediator.Send(new DeleteAccessoryCommand
        {
            AccessoryId = id,
            CurrentUserId = currentUserId
        });

        if (!result.Success)
            return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });

        return Ok(new { status = "success", message = result.Message });
    }

    // ==================== CHECKOUT (via CQRS Command) ====================

    [HttpPost("{id:guid}/checkout")]
    [Authorize(Policy = "accessories.checkout")]
    public async Task<IActionResult> Checkout(Guid id, [FromBody] CheckoutAccessoryRequest r)
    {
        var currentUserId = GetCurrentUserId();

        var result = await _mediator.Send(new CheckoutAccessoryCommand
        {
            AccessoryId = id,
            CheckoutType = r.CheckoutType,
            TargetId = r.TargetId,
            Quantity = r.Quantity,
            Note = r.Note,
            CurrentUserId = currentUserId
        });

        if (!result.Success)
            return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });

        return Ok(new { status = "success", message = result.Message, data = new { Id = result.AccessoryId } });
    }

    // ==================== CHECKIN (via CQRS Command) ====================

    [HttpPost("checkouts/{checkoutId:guid}/checkin")]
    [Authorize(Policy = "accessories.checkout")]
    public async Task<IActionResult> Checkin(Guid checkoutId, [FromBody] CheckinAccessoryRequest r)
    {
        var currentUserId = GetCurrentUserId();

        var result = await _mediator.Send(new CheckinAccessoryCommand
        {
            CheckoutId = checkoutId,
            ReturnQty = r.ReturnQty,
            Note = r.Note,
            CurrentUserId = currentUserId
        });

        if (!result.Success)
            return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });

        return Ok(new { status = "success", message = result.Message });
    }

    // ==================== GET CHECKOUTS HISTORY ====================
    // [FIX C 2026-10-02] Moved to GetAccessoryCheckoutsQuery (same scope rule, projection, ordering
    // and per-row target-name resolution) — this was the last EF-direct action of the controller.

    [HttpGet("{id:guid}/checkouts")]
    [Authorize(Policy = "accessories.view")]
    public async Task<IActionResult> GetCheckouts(Guid id)
    {
        var result = await _mediator.Send(new GetAccessoryCheckoutsQuery(id));

        if (!result.Found)
            return NotFound(new { status = "error", message = "Accessory not found." });

        return Ok(new { status = "success", data = result.Items });
    }

    // ==================== HELPERS ====================

    private Guid GetCurrentUserId()
    {
        // Read the local DB user ID from the "local_user_id" claim,
        // injected by the JIT provisioning hook in OnTokenValidated.
        return _currentUserService.GetLocalUserId();
    }
}

// ==================== REQUEST DTOs ====================

public record CreateAccessoryRequest(
    string Name, string? ItemNo, int Qty, int MinAmt,
    Guid? CategoryId, Guid? ManufacturerId, Guid? SupplierId,
    Guid? LocationId, Guid? CompanyId,
    string? ModelNumber, string? OrderNumber,
    decimal? PurchaseCost, DateTime? PurchaseDate, string? Notes, string? Image);

/// <summary>
/// Patch-style Update DTO (Task M2): every field nullable so a partial payload only changes the fields
/// explicitly sent, without wiping the others. Distinct from the Create DTO (whose Name/Qty/MinAmt are
/// required) — the two intents must not share one non-nullable DTO.
/// </summary>
public record UpdateAccessoryRequest(
    string? Name = null, string? ItemNo = null, int? Qty = null, int? MinAmt = null,
    Guid? CategoryId = null, Guid? ManufacturerId = null, Guid? SupplierId = null,
    Guid? LocationId = null, Guid? CompanyId = null,
    string? ModelNumber = null, string? OrderNumber = null,
    decimal? PurchaseCost = null, DateTime? PurchaseDate = null, string? Notes = null, string? Image = null);

public record CheckoutAccessoryRequest(
    AccessoryCheckoutType CheckoutType,
    Guid TargetId,
    int Quantity,
    string? Note);

public record CheckinAccessoryRequest(
    int ReturnQty,
    string? Note);