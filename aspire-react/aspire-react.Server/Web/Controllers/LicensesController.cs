using System.Security.Claims;
using aspire_react.Server.Application.Licenses.Commands;
using aspire_react.Server.Application.Licenses.Queries;
using aspire_react.Server.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// [Giai đoạn 3 — Nặng] Licenses migrated to MediatR — 14 endpoints. CheckoutSeat owns the
/// FOR UPDATE seat-mutex transaction verbatim (Task O-FIX preserved); Create/Update/Delete/
/// Checkin log via IActionLogService (thin Log, same SaveChanges — verbatim). Legacy assign/
/// remove aliases forward to the seat commands (verbatim translation logic).
/// Error mapping verbatim: NOT_FOUND → 404; structured codes → 400 + error_code.
/// </summary>
[ApiController]
[Route("api/v1/licenses")]
public class LicensesController : ControllerBase
{
    private readonly IMediator _mediator;
    public LicensesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    private Guid GetCurrentUserId()
    {
        // [SEC-FIX CLAIM-CLEANUP, 2026-08-23] ONLY "local_user_id" (JIT-stamped). Keycloak
        // sub/preferred_username are never a user identity source (bug-class 1). Absent/empty
        // HttpContext (tests without ControllerContext) → Guid.Empty (fail closed).
        if (Guid.TryParse(HttpContext?.User?.FindFirstValue("local_user_id") ?? string.Empty, out var local)) return local;
        return Guid.Empty;
    }

    // ==================== LIST ====================

    [HttpGet]
    [Authorize(Policy = "licenses.view")]
    public async Task<IActionResult> GetLicenses([FromQuery] string? search, [FromQuery] Guid? categoryId,
        [FromQuery] Guid? companyId, [FromQuery] bool expiringSoon = false, [FromQuery] bool lowSeats = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _mediator.Send(new ListLicensesQuery(search, categoryId, companyId, expiringSoon, lowSeats, page, pageSize));
        return Ok(new { status = "success", data = result.Items, pagination = new { page, pageSize, totalItems = result.Total, totalPages = (int)Math.Ceiling((double)result.Total / pageSize), hasNextPage = page * pageSize < result.Total, hasPreviousPage = page > 1 } });
    }

    // ==================== DETAIL + SEATS ====================

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "licenses.view")]
    public async Task<IActionResult> GetLicense(Guid id)
    {
        var l = await _mediator.Send(new GetLicenseByIdQuery(id));
        if (l == null) return NotFound(new { status = "error", message = "License not found." });
        return Ok(new { status = "success", data = l });
    }

    [HttpGet("{id:guid}/seats")]
    [Authorize(Policy = "licenses.view")]
    public async Task<IActionResult> GetSeats(Guid id)
    {
        var seats = await _mediator.Send(new GetLicenseSeatsQuery(id));
        if (seats == null) return NotFound(new { status = "error", message = "License not found." });
        return Ok(new { status = "success", data = seats });
    }

    [HttpGet("for-user/{userId:guid}")]
    [Authorize(Policy = "licenses.view")]
    public async Task<IActionResult> GetLicensesForUser(Guid userId)
        => Ok(new { status = "success", data = await _mediator.Send(new GetLicensesForTargetQuery(LicenseTargetKind.User, userId)) });

    [HttpGet("for-asset/{assetId:guid}")]
    [Authorize(Policy = "licenses.view")]
    public async Task<IActionResult> GetLicensesForAsset(Guid assetId)
        => Ok(new { status = "success", data = await _mediator.Send(new GetLicensesForTargetQuery(LicenseTargetKind.Asset, assetId)) });

    [HttpGet("for-system/{systemInfoId:guid}")]
    [Authorize(Policy = "licenses.view")]
    public async Task<IActionResult> GetLicensesForSystem(Guid systemInfoId)
        => Ok(new { status = "success", data = await _mediator.Send(new GetLicensesForTargetQuery(LicenseTargetKind.SystemInfo, systemInfoId)) });

    // ==================== CREATE ====================

    [HttpPost]
    [Authorize(Policy = "licenses.create")]
    public async Task<IActionResult> Create([FromBody] CreateLicenseRequest r)
    {
        var result = await _mediator.Send(new CreateLicenseCommand(
            r.Name, r.Serial, r.Seats, r.Reassignable, r.ExpirationDate, r.TerminationDate,
            r.PurchaseCost, r.PurchaseDate, r.OrderNumber, r.MinSeats, r.Notes,
            r.SupplierId, r.ManufacturerId, r.CategoryId, r.CompanyId, GetCurrentUserId()));
        if (!result.Success) return MapFailure(result);
        return Ok(new { status = "success", message = "License created.", data = new { result.Id, result.Name } });
    }

    // ==================== UPDATE (whitelist + seat sync) ====================

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "licenses.edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateLicenseRequest r)
    {
        var result = await _mediator.Send(new UpdateLicenseCommand(
            id, r.Name, r.Serial, r.Seats, r.Reassignable, r.ExpirationDate, r.TerminationDate,
            r.PurchaseCost, r.PurchaseDate, r.OrderNumber, r.MinSeats, r.Notes,
            r.SupplierId, r.ManufacturerId, r.CategoryId, r.CompanyId, GetCurrentUserId()));
        if (!result.Success) return MapFailure(result);
        return Ok(new { status = "success", message = "License updated." });
    }

    // ==================== DELETE (guard) ====================

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "licenses.delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await _mediator.Send(new DeleteLicenseCommand(id, GetCurrentUserId()));
        if (!result.Success) return MapFailure(result);
        return Ok(new { status = "success", message = "License deleted." });
    }

    // ==================== CHECKOUT / CHECKIN ====================

    [HttpPost("{id:guid}/checkout")]
    [Authorize(Policy = "licenses.checkout")]
    public async Task<IActionResult> CheckoutSeat(Guid id, [FromBody] CheckoutLicenseSeatRequest r)
    {
        var result = await _mediator.Send(new CheckoutLicenseSeatCommand(id, r.SeatId, r.TargetType, r.TargetId, r.Note, GetCurrentUserId()));
        if (!result.Success)
        {
            return result.ErrorCode == "NOT_FOUND"
                ? NotFound(new { status = "error", message = result.Message })
                : BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        }
        return Ok(new { status = "success", message = "Seat assigned.", data = new { seat = result.SeatId } });
    }

    [HttpPost("{id:guid}/checkin")]
    [Authorize(Policy = "licenses.checkout")]
    public async Task<IActionResult> CheckinSeat(Guid id, [FromBody] CheckinLicenseSeatRequest r)
    {
        var result = await _mediator.Send(new CheckinLicenseSeatCommand(id, r.SeatId, GetCurrentUserId()));
        if (!result.Success)
        {
            return result.ErrorCode == "NOT_FOUND"
                ? NotFound(new { status = "error", message = result.Message })
                : BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        }
        return Ok(new { status = "success", message = "Seat checked in." });
    }

    // ==================== Legacy aliases (assign/remove) ====================

    [HttpPost("{id:guid}/assign")]
    [Authorize(Policy = "licenses.checkout")]
    public async Task<IActionResult> AssignSeatLegacy(Guid id, [FromBody] AssignSeatRequest r)
    {
        if (r.UserId.HasValue && r.AssetId.HasValue)
            return BadRequest(new { status = "error", message = "Phải chọn đúng 1 đối tượng nhận.", error_code = "SEAT_TARGET_AMBIGUOUS" });
        if (!r.UserId.HasValue && !r.AssetId.HasValue)
            return BadRequest(new { status = "error", message = "Cần chọn đối tượng nhận.", error_code = "TARGET_REQUIRED" });
        var targetType = r.UserId.HasValue ? LicenseSeatTargetType.User : LicenseSeatTargetType.Asset;
        return await CheckoutSeat(id, new CheckoutLicenseSeatRequest(r.SeatId, targetType, r.UserId ?? r.AssetId, r.Note));
    }

    [HttpPost("{id:guid}/remove")]
    [Authorize(Policy = "licenses.checkout")]
    public async Task<IActionResult> RemoveSeatLegacy(Guid id, [FromBody] AssignSeatRequest r)
        => await CheckinSeat(id, new CheckinLicenseSeatRequest(r.SeatId));

    private IActionResult MapFailure(LicenseResult result)
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
public record CreateLicenseRequest(string Name, string? Serial, int Seats, bool? Reassignable = null, DateTime? ExpirationDate = null,
    DateTime? TerminationDate = null, decimal? PurchaseCost = null, DateTime? PurchaseDate = null, string? OrderNumber = null,
    int? MinSeats = null, string? Notes = null, Guid? SupplierId = null, Guid? ManufacturerId = null, Guid? CategoryId = null, Guid? CompanyId = null);

public record UpdateLicenseRequest(string? Name = null, string? Serial = null, int? Seats = null, bool? Reassignable = null,
    DateTime? ExpirationDate = null, DateTime? TerminationDate = null, decimal? PurchaseCost = null, DateTime? PurchaseDate = null,
    string? OrderNumber = null, int? MinSeats = null, string? Notes = null, Guid? SupplierId = null, Guid? ManufacturerId = null,
    Guid? CategoryId = null, Guid? CompanyId = null);

public record CheckoutLicenseSeatRequest(Guid? SeatId, LicenseSeatTargetType TargetType, Guid? TargetId, string? Note = null);
public record CheckinLicenseSeatRequest(Guid SeatId);
public record AssignSeatRequest(Guid SeatId, Guid? AssetId, Guid? UserId, string? Note = null); // legacy
