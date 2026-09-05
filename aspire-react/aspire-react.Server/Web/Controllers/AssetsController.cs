using System.Security.Claims;
using System.Text.Json;
using aspire_react.Server.Application.Assets.Commands;
using aspire_react.Server.Application.Assets.DTOs;
using aspire_react.Server.Application.Assets.Queries;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Web.Controllers;

[ApiController]
[Route("api/v1/assets")]
public class AssetsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICompanyScopeService _companyScope;

    public AssetsController(AppDbContext context, IMediator mediator, ICurrentUserService currentUserService, ICompanyScopeService companyScope)
    {
        _context = context;
        _mediator = mediator;
        _currentUserService = currentUserService;
        _companyScope = companyScope;
    }

    private Guid GetCurrentUserId() => _currentUserService.GetLocalUserId();

    private Task<Guid?> GetUserCompanyIdAsync() => _companyScope.GetCurrentUserCompanyIdAsync();

    // ==================== LIST ====================
    // [Giai đoạn 3 — Assets, section CUỐI] Thin MediatR mapping over ListAssetsQuery (filters +
    // scope + pagination + batch assignedTo name-resolve verbatim trong handler). QUIRK verbatim:
    // list assignedTo.type = "User"/"Department"/"SystemPosition" (hoa) — KHÔNG thống nhất với
    // detail ("user" thường) — pre-existing behavior, giữ nguyên (user-approved).

    [HttpGet]
    [Authorize(Policy = "assets.view")]
    public async Task<IActionResult> GetAssets(
        [FromQuery] string? search, [FromQuery] AssetStatus? status, [FromQuery] Guid? categoryId,
        [FromQuery] Guid? locationId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _mediator.Send(new ListAssetsQuery(search, status, categoryId, locationId, page, pageSize));

        return Ok(new { status = "success", data = result.Items, pagination = new { page, pageSize, totalItems = result.Total, totalPages = (int)Math.Ceiling((double)result.Total / pageSize), hasNextPage = page * pageSize < result.Total, hasPreviousPage = page > 1 } });
    }

    // ==================== BY ID ====================
    // [Giai đoạn 3 — Assets] Thin MediatR mapping over GetAssetByIdQuery (scope 404 + 26-key shape +
    // QUIRK verbatim: detail assignedTo type lowercase "user"/"department"/"systemPosition" với
    // per-type shapes — KHÔNG thống nhất với list).

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "assets.view")]
    public async Task<IActionResult> GetAsset(Guid id)
    {
        var result = await _mediator.Send(new GetAssetByIdQuery(id));
        if (!result.Success || result.Asset == null)
            return NotFound(new { status = "error", message = "Asset not found." });

        return Ok(new { status = "success", data = result.Asset });
    }

    // ==================== CREATE ====================

    [HttpPost]
    [Authorize(Policy = "assets.create")]
    public async Task<IActionResult> CreateAsset([FromBody] CreateAssetRequest r)
    {
        var userId = GetCurrentUserId();
        var result = await _mediator.Send(new CreateAssetCommand
        {
            AssetTag = r.AssetTag,
            Name = r.Name,
            Serial = r.Serial,
            ModelId = r.ModelId,
            LocationId = r.LocationId,
            SupplierId = r.SupplierId,
            CompanyId = r.CompanyId,
            PurchaseCost = r.PurchaseCost,
            PurchaseDate = r.PurchaseDate,
            WarrantyMonths = r.WarrantyMonths,
            OrderNumber = r.OrderNumber,
            Notes = r.Notes,
            Physical = r.Physical,
            Requestable = r.Requestable,
            CurrentUserId = userId
        });
        if (!result.Success) return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        return CreatedAtAction(nameof(GetAsset), new { id = result.AssetId }, new { status = "success", message = result.Message, data = new { Id = result.AssetId } });
    }

    // ==================== UPDATE ====================

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "assets.edit")]
    public async Task<IActionResult> UpdateAsset(Guid id, [FromBody] UpdateAssetRequest r)
    {
        var userId = GetCurrentUserId();
        var result = await _mediator.Send(new UpdateAssetCommand
        {
            Id = id,
            AssetTag = r.AssetTag ?? string.Empty,
            Name = r.Name,
            Serial = r.Serial,
            Image = r.Image,
            ModelId = r.ModelId,
            LocationId = r.LocationId,
            SupplierId = r.SupplierId,
            CompanyId = r.CompanyId,
            PurchaseCost = r.PurchaseCost,
            PurchaseDate = r.PurchaseDate,
            WarrantyMonths = r.WarrantyMonths,
            OrderNumber = r.OrderNumber,
            Notes = r.Notes,
            Physical = r.Physical,
            Requestable = r.Requestable,
            CurrentUserId = userId
        });
        if (!result.Success) return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        return Ok(new { status = "success", message = result.Message });
    }

    // ==================== DELETE ====================

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "assets.delete")]
    public async Task<IActionResult> DeleteAsset(Guid id)
    {
        var userId = GetCurrentUserId();
        var result = await _mediator.Send(new DeleteAssetCommand { AssetId = id, CurrentUserId = userId });
        if (!result.Success) return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        return Ok(new { status = "success", message = result.Message });
    }

    // ==================== CONFIRM ====================

    [HttpPost("{id:guid}/confirm")]
    [Authorize(Policy = "assets.edit")]
    public async Task<IActionResult> Confirm(Guid id)
    {
        var userId = GetCurrentUserId();
        var result = await _mediator.Send(new ConfirmAssetCommand(id, userId));
        if (!result.Success) return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        return Ok(new { status = "success", message = result.Message });
    }

    // ==================== CHECKOUT ====================

    [HttpPost("{id:guid}/checkout")]
    [Authorize(Policy = "assets.checkout")]
    public async Task<IActionResult> Checkout(Guid id, [FromBody] CheckoutRequestDto request)
    {
        var userId = GetCurrentUserId();
        var result = await _mediator.Send(new CheckoutAssetCommand(
            id, request.TargetType, request.TargetId, request.Note,
            request.CheckoutAt, request.LocationId, userId));
        if (!result.Success) return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        return Ok(new { status = "success", message = result.Message, data = new { assignment_id = result.Assignment?.Id } });
    }

    // ==================== CHECKIN ====================

    [HttpPost("{id:guid}/checkin")]
    [Authorize(Policy = "assets.checkin")]
    public async Task<IActionResult> Checkin(Guid id, [FromBody] CheckinRequestDto request)
    {
        var userId = GetCurrentUserId();
        var result = await _mediator.Send(new CheckinAssetCommand(id, request.LocationId, request.Note, request.CheckinAt, userId));
        if (!result.Success) return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        return Ok(new { status = "success", message = result.Message });
    }

    // ==================== ARCHIVE / UNARCHIVE ====================

    [HttpPost("{id:guid}/archive")]
    [Authorize(Policy = "assets.edit")]
    public async Task<IActionResult> Archive(Guid id, [FromBody] ArchiveUnarchiveRequestDto request)
    {
        var userId = GetCurrentUserId();
        var result = await _mediator.Send(new ArchiveAssetCommand(id, request.LocationId, userId, request.Note));
        if (!result.Success) return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        return Ok(new { status = "success", message = result.Message });
    }

    [HttpPost("{id:guid}/unarchive")]
    [Authorize(Policy = "assets.edit")]
    public async Task<IActionResult> Unarchive(Guid id, [FromBody] ArchiveUnarchiveRequestDto request)
    {
        var userId = GetCurrentUserId();
        var result = await _mediator.Send(new UnarchiveAssetCommand(id, userId, request.Note));
        if (!result.Success) return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        return Ok(new { status = "success", message = result.Message });
    }

    // ==================== HISTORY ====================
    // [Giai đoạn 3 — Assets] Thin MediatR mapping over GetAssetHistoryQuery (Task K scope-404 +
    // Take(50) + Creator JOIN verbatim trong handler).

    [HttpGet("{id:guid}/history")]
    [Authorize(Policy = "assets.view")]
    public async Task<IActionResult> GetHistory(Guid id)
    {
        var result = await _mediator.Send(new GetAssetHistoryQuery(id));
        if (!result.Success || result.Logs == null)
            return NotFound(new { status = "error", message = "Asset not found." });

        return Ok(new { status = "success", data = result.Logs });
    }

    // ==================== AUDIT ====================

    [HttpPost("{id:guid}/audit")]
    [Authorize(Policy = "assets.audit")]
    public async Task<IActionResult> Audit(Guid id, [FromBody] AuditRequestDto request)
    {
        var userId = GetCurrentUserId();
        var result = await _mediator.Send(new AuditAssetCommand(id, request.AuditDate, request.Note, userId));
        if (!result.Success) return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        return Ok(new { status = "success", message = result.Message });
    }
}

// ==================== DTOs ====================

public record CreateAssetRequest(string AssetTag, string Name, string? Serial, Guid? ModelId,
    Guid? LocationId, Guid? SupplierId, Guid? CompanyId, decimal? PurchaseCost,
    DateTime? PurchaseDate, int? WarrantyMonths, string? OrderNumber, string? Notes,
    bool Physical = true, bool Requestable = false, string? Image = null);

public record UpdateAssetRequest(string? AssetTag, string Name, string? Serial, Guid? ModelId,
    Guid? LocationId, Guid? SupplierId, Guid? CompanyId, decimal? PurchaseCost,
    DateTime? PurchaseDate, int? WarrantyMonths, string? OrderNumber, string? Notes,
    bool? Physical = null, bool? Requestable = null, string? Image = null);

public record CheckoutRequestDto(AssignmentTargetType TargetType, Guid TargetId,
    string? Note = null, DateTime? CheckoutAt = null, Guid? LocationId = null);

public record CheckinRequestDto(Guid LocationId, string? Note = null, DateTime? CheckinAt = null);

public record ArchiveUnarchiveRequestDto(Guid LocationId, string? Note = null);

public record AuditRequestDto(DateTime? AuditDate = null, string? Note = null);