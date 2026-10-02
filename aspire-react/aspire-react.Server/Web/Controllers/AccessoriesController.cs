using System.Security.Claims;
using System.Text.Json;
using aspire_react.Server.Application.Accessories.Commands;
using aspire_react.Server.Application.Accessories.Queries;
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
[Route("api/v1/accessories")]
public class AccessoriesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICompanyScopeService _companyScope;
    public AccessoriesController(AppDbContext context, IMediator mediator, ICurrentUserService currentUserService, ICompanyScopeService companyScope)
    {
        _context = context;
        _mediator = mediator;
        _currentUserService = currentUserService;
        _companyScope = companyScope;
    }

    private Task<Guid?> GetUserCompanyIdAsync() => _companyScope.GetCurrentUserCompanyIdAsync();

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

    [HttpGet("{id:guid}/checkouts")]
    [Authorize(Policy = "accessories.view")]
    public async Task<IActionResult> GetCheckouts(Guid id)
    {
        // Company scoping: a regular user may only view the checkouts of an accessory in their company.
        var userCompanyId = await GetUserCompanyIdAsync();
        var visible = await _context.Accessories.AsNoTracking()
            .AnyAsync(a => a.Id == id && (userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value));
        if (!visible) return NotFound(new { status = "error", message = "Accessory not found." });

        var checkouts = await _context.AccessoryCheckouts
            .Include(ch => ch.CreatedByUser)
            .Where(ch => ch.AccessoryId == id)
            .OrderByDescending(ch => ch.CheckedOutAt)
            .Select(ch => new
            {
                ch.Id,
                ch.AccessoryId,
                ch.CheckoutType,
                ch.TargetId,
                ch.AssignedQty,
                ch.ReturnedQty,
                RemainingOut = ch.AssignedQty - ch.ReturnedQty,
                ch.Note,
                ch.CheckedOutAt,
                CreatedByUserId = ch.CreatedByUserId,
                CreatedByName = ch.CreatedByUser != null ? ch.CreatedByUser.Username : null,
                CreatedByFirstName = ch.CreatedByUser != null ? ch.CreatedByUser.FirstName : null,
                CreatedByLastName = ch.CreatedByUser != null ? ch.CreatedByUser.LastName : null
            })
            .ToListAsync();

        var enriched = checkouts.Select(ch => new
        {
            ch.Id,
            ch.AccessoryId,
            ch.CheckoutType,
            ch.TargetId,
            TargetName = ResolveTargetName(ch.CheckoutType, ch.TargetId),
            ch.AssignedQty,
            ch.ReturnedQty,
            ch.RemainingOut,
            ch.Note,
            ch.CheckedOutAt,
            ch.CreatedByUserId,
            ch.CreatedByName,
            ch.CreatedByFirstName,
            ch.CreatedByLastName
        }).ToList();

        return Ok(new { status = "success", data = enriched });
    }

    // ==================== HELPERS ====================

    private string? ResolveTargetName(AccessoryCheckoutType type, Guid targetId)
    {
        return type switch
        {
            AccessoryCheckoutType.User => _context.Users.Where(u => u.Id == targetId)
                .AsNoTracking()
                .Select(u => (u.FirstName + " " + u.LastName).Trim() != "" ? (u.FirstName + " " + u.LastName).Trim() : u.Username)
                .FirstOrDefault(),
            AccessoryCheckoutType.Department => _context.Departments.Where(d => d.Id == targetId).AsNoTracking().Select(d => d.Name).FirstOrDefault(),
            AccessoryCheckoutType.Location => _context.Locations.Where(l => l.Id == targetId).AsNoTracking().Select(l => l.Name).FirstOrDefault(),
            AccessoryCheckoutType.SystemPosition => _context.SystemPositions.Where(sp => sp.Id == targetId).AsNoTracking().Select(sp => sp.Name).FirstOrDefault(),
            _ => null
        };
    }

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