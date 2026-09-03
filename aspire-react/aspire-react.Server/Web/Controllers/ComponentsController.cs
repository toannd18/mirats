using System.Security.Claims;
using aspire_react.Server.Application.Components.Commands;
using aspire_react.Server.Application.Components.Queries;
using aspire_react.Server.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// [Giai đoạn 3 — Nặng] Components migrated to MediatR — 11 endpoints (3 queries + 8 commands).
/// ⚠️ TRANSACTION-BOUNDARY: the allocation commands own the Npgsql strategy + explicit transaction
/// (moved verbatim from RunTransactional) — the IComponentAllocationService FOR UPDATE lock +
/// SaveChanges commit rely on this boundary; the service itself is untouched.
/// CUD logs written via IActionLogService INSIDE the same SaveChanges/transaction as data
/// (verbatim ordering — NOT ILoggableCommand for this controller).
/// Error mapping verbatim: NOT_FOUND → 404; COMPONENT_HAS_ALLOCATION_HISTORY / FIELD_LOCKED /
/// CATEGORY_REQUIRED / INVALID_CATEGORY / COMPANY_REQUIRED / INVALID_COMPANY / COMPANY_MISMATCH
/// → 400 WITH error_code; regex/qty messages → 400 WITHOUT error_code.
/// </summary>
[ApiController]
[Route("api/v1/components")]
public class ComponentsController : ControllerBase
{
    private readonly IMediator _mediator;
    public ComponentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    private Guid GetCurrentUserId()
    {
        // [SEC-FIX CLAIM-CLEANUP, 2026-08-23] ONLY "local_user_id" (JIT-stamped). Keycloak
        // sub/preferred_username are never a user identity source (bug-class 1). Absent → Guid.Empty.
        if (Guid.TryParse(User.FindFirstValue("local_user_id"), out var local)) return local;
        return Guid.Empty;
    }

    [HttpGet]
    [Authorize(Policy = "components.view")]
    public async Task<IActionResult> GetComponents([FromQuery] string? search, [FromQuery] Guid? categoryId,
        [FromQuery] Guid? companyId, [FromQuery] Guid? locationId,
        [FromQuery] bool uncategorized = false, [FromQuery] bool uncompanied = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _mediator.Send(new ListComponentsQuery(search, categoryId, companyId, locationId, uncategorized, uncompanied, page, pageSize));
        return Ok(new { status = "success", data = result.Items, pagination = new { page, pageSize, totalItems = result.Total, totalPages = (int)Math.Ceiling((double)result.Total / pageSize), hasNextPage = page * pageSize < result.Total, hasPreviousPage = page > 1 } });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "components.view")]
    public async Task<IActionResult> GetComponent(Guid id)
    {
        var c = await _mediator.Send(new GetComponentByIdQuery(id));
        if (c == null) return NotFound(new { status = "error", message = "Component not found." });
        return Ok(new { status = "success", data = c });
    }

    [HttpPost]
    [Authorize(Policy = "components.create")]
    public async Task<IActionResult> Create([FromBody] CreateComponentRequest r)
    {
        var result = await _mediator.Send(new CreateComponentCommand(
            r.Name, r.Serial, r.Qty, r.MinAmt, r.CategoryId, r.LocationId, r.CompanyId,
            r.SupplierId, r.ManufacturerId, r.ModelNumber, r.OrderNumber, r.PurchaseCost,
            r.PurchaseDate, r.Notes, r.TrackingType, r.SerialNumbers, GetCurrentUserId()));
        if (!result.Success)
            return MapFailure(result.Success, result.Message, result.ErrorCode);
        return CreatedAtAction(nameof(GetComponent), new { id = result.Id },
            new { status = "success", message = "Component created.", data = new { result.Id, result.Name, result.Qty, TrackingType = result.TrackingType } });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "components.edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateComponentRequest r)
    {
        var result = await _mediator.Send(new UpdateComponentCommand(
            id, r.Name, r.Notes, r.SupplierId, r.ManufacturerId, r.ModelNumber, r.MinAmt,
            r.LocationId, r.OrderNumber, r.PurchaseCost, r.PurchaseDate, r.CategoryId,
            r.CompanyId, r.TrackingType, GetCurrentUserId()));
        if (!result.Success) return MapFailure(result.Success, result.Message, result.ErrorCode);
        return Ok(new { status = "success", message = "Component updated." });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "components.delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await _mediator.Send(new DeleteComponentCommand(id, GetCurrentUserId()));
        if (!result.Success) return MapFailure(result.Success, result.Message, result.ErrorCode);
        return Ok(new { status = "success", message = "Component deleted." });
    }

    // ==================== Legacy quantity endpoints (kept for backward compatibility) ====================

    [HttpPost("{id:guid}/assign")]
    [Authorize(Policy = "components.checkout")]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignComponentRequest r)
    {
        var result = await _mediator.Send(new AssignComponentCommand(id, r.AssetId, r.AssignedQty, r.Note, GetCurrentUserId()));
        if (!result.Success) return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        return Ok(new { status = "success", message = result.Message });
    }

    [HttpPost("{id:guid}/remove")]
    [Authorize(Policy = "components.checkout")]
    public async Task<IActionResult> RemoveAssignment(Guid id, [FromBody] RemoveComponentRequest r)
    {
        var result = await _mediator.Send(new RemoveComponentAssignmentCommand(id, r.AssignmentId, GetCurrentUserId()));
        if (!result.Success) return MapFailure(result.Success, result.Message, result.ErrorCode);
        return Ok(new { status = "success", message = "Component assignment removed." });
    }

    // ==================== Serial & Bulk unified endpoints ====================

    [HttpGet("{id:guid}/units")]
    [Authorize(Policy = "components.view")]
    public async Task<IActionResult> GetUnits(Guid id, [FromQuery] ComponentUnitStatus? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _mediator.Send(new GetComponentUnitsQuery(id, status, page, pageSize));
        if (result == null) return NotFound(new { status = "error", message = "Component not found." });
        return Ok(new { status = "success", data = result.Items, pagination = new { page, pageSize, totalItems = result.Total, totalPages = (int)Math.Ceiling((double)result.Total / pageSize), hasNextPage = page * pageSize < result.Total, hasPreviousPage = page > 1 } });
    }

    [HttpPost("{id:guid}/units")]
    [Authorize(Policy = "components.edit")]
    public async Task<IActionResult> StockInUnits(Guid id, [FromBody] StockInUnitsRequest r)
    {
        var result = await _mediator.Send(new StockInUnitsCommand(id, r.SerialNumbers, r.Note, GetCurrentUserId()));
        if (!result.Success) return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        return Ok(new { status = "success", message = result.Message });
    }

    [HttpPost("{id:guid}/checkout")]
    [Authorize(Policy = "components.checkout")]
    public async Task<IActionResult> Checkout(Guid id, [FromBody] CheckoutComponentRequest r)
    {
        var result = await _mediator.Send(new CheckoutComponentCommand(id, r.AssetId, r.Quantity, r.SerialNo, r.Note, GetCurrentUserId()));
        if (!result.Success) return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        return Ok(new { status = "success", message = result.Message });
    }

    [HttpPost("{id:guid}/checkin")]
    [Authorize(Policy = "components.checkout")]
    public async Task<IActionResult> Checkin(Guid id, [FromBody] CheckinComponentRequest r)
    {
        var result = await _mediator.Send(new CheckinComponentCommand(id, r.AssetId, r.Quantity, r.SerialNo, r.Note, GetCurrentUserId()));
        if (!result.Success) return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        return Ok(new { status = "success", message = result.Message });
    }

    /// <summary>
    /// Maps a failure to the EXACT same HTTP bodies as the pre-migration controller:
    /// NOT_FOUND → 404; error codes that the old bodies carried WITH error_code → 400 + error_code;
    /// plain messages (qty/regex) → 400 WITHOUT error_code.
    /// </summary>
    private IActionResult MapFailure(bool successIgnored, string message, string? errorCode)
    {
        if (errorCode == "NOT_FOUND")
            return NotFound(new { status = "error", message });

        object body = errorCode is null
            ? new { status = "error", message }
            : new { status = "error", message, error_code = errorCode };
        return BadRequest(body);
    }
}

/// <summary>Request DTOs — verbatim from the pre-migration records.</summary>
public record CreateComponentRequest(string Name, string? Serial, int? Qty, int MinAmt, Guid? CategoryId,
    Guid? LocationId, Guid? CompanyId, Guid? SupplierId, Guid? ManufacturerId, string? ModelNumber,
    string? OrderNumber, decimal? PurchaseCost, DateTime? PurchaseDate, string? Notes,
    TrackingType TrackingType = TrackingType.Bulk, List<string>? SerialNumbers = null);
public record UpdateComponentRequest(string? Name = null, string? Notes = null, Guid? SupplierId = null,
    Guid? ManufacturerId = null, string? ModelNumber = null, int? MinAmt = null, Guid? LocationId = null,
    string? OrderNumber = null, decimal? PurchaseCost = null, DateTime? PurchaseDate = null,
    // Locked-field detection (rejected if DIFFERENT from current DB value)
    TrackingType? TrackingType = null, Guid? CategoryId = null, Guid? CompanyId = null,
    // Always ignored
    int? Qty = null, string? Serial = null, string? ItemNo = null);
public record AssignComponentRequest(Guid AssetId, int AssignedQty, string? Note);
public record RemoveComponentRequest(Guid AssignmentId);
public record StockInUnitsRequest(List<string> SerialNumbers, string? Note = null);
public record CheckoutComponentRequest(Guid AssetId, int Quantity = 0, string? SerialNo = null, string? Note = null);
public record CheckinComponentRequest(Guid? AssetId = null, int Quantity = 0, string? SerialNo = null, string? Note = null);
