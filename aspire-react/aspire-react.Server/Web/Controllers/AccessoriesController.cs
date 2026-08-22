using System.Security.Claims;
using System.Text.Json;
using aspire_react.Server.Application.Accessories.Commands;
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

    [HttpGet]
    [Authorize(Policy = "accessories.view")]
    public async Task<IActionResult> GetAccessories([FromQuery] string? search, [FromQuery] Guid? categoryId,
        [FromQuery] Guid? locationId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var query = _context.Accessories.Include(a => a.Checkouts).Include(a => a.Category)
            .Include(a => a.Location).Include(a => a.Company).AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(a => a.Name.ToLower().Contains(s) || (a.ItemNo != null && a.ItemNo.ToLower().Contains(s)));
        }
        if (categoryId.HasValue) query = query.Where(a => a.CategoryId == categoryId);
        if (locationId.HasValue) query = query.Where(a => a.LocationId == locationId);

        var userCompanyId = await GetUserCompanyIdAsync();
        query = query.Where(a => userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value);

        var total = await query.CountAsync();
        var items = await query.OrderBy(a => a.Name).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new {
                a.Id, a.Name, a.ItemNo, a.Notes, a.Qty, a.MinAmt,
                a.CompanyId,
                CompanyName = a.Company != null ? a.Company.Name : null,
                Remaining = a.Qty - a.Checkouts.Sum(ch => ch.AssignedQty - ch.ReturnedQty),
                CheckedOutQty = a.Checkouts.Sum(ch => ch.AssignedQty - ch.ReturnedQty),
                IsLowStock = (a.Qty - a.Checkouts.Sum(ch => ch.AssignedQty - ch.ReturnedQty)) <= a.MinAmt,
                Category = a.Category == null ? null : new { a.Category.Id, a.Category.Name },
                Location = a.Location == null ? null : new { a.Location.Id, a.Location.Name }
            }).ToListAsync();

        return Ok(new { status = "success", data = items, pagination = new { page, pageSize, totalItems = total, totalPages = (int)Math.Ceiling((double)total / pageSize), hasNextPage = page * pageSize < total, hasPreviousPage = page > 1 } });
    }

    // ==================== GET BY ID ====================

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "accessories.view")]
    public async Task<IActionResult> GetAccessory(Guid id)
    {
        var userCompanyId = await GetUserCompanyIdAsync();
        var a = await _context.Accessories.Include(x => x.Checkouts).Include(x => x.Category)
            .Include(x => x.Manufacturer).Include(x => x.Supplier).Include(x => x.Location)
            .Include(x => x.Company).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (a == null || (userCompanyId.HasValue && a.CompanyId.HasValue && a.CompanyId.Value != userCompanyId.Value))
            return NotFound(new { status = "error", message = "Accessory not found." });

        var remaining = a.Qty - a.Checkouts.Sum(ch => ch.AssignedQty - ch.ReturnedQty);
        return Ok(new { status = "success", data = new {
            a.Id, a.Name, a.ItemNo, a.Qty, a.MinAmt,
            a.ModelNumber, a.OrderNumber, a.PurchaseDate, a.PurchaseCost, a.Notes,
            a.CategoryId, a.ManufacturerId, a.SupplierId, a.LocationId, a.CompanyId,
            Remaining = remaining, PercentRemaining = a.Qty > 0 ? Math.Round((double)remaining / a.Qty * 100, 2) : 0,
            IsLowStock = remaining <= a.MinAmt,
            CheckedOutQty = a.Checkouts.Sum(ch => ch.AssignedQty - ch.ReturnedQty),
            Category = a.Category == null ? null : new { a.Category.Id, a.Category.Name },
            Manufacturer = a.Manufacturer == null ? null : new { a.Manufacturer.Id, a.Manufacturer.Name },
            Supplier = a.Supplier == null ? null : new { a.Supplier.Id, a.Supplier.Name },
            Location = a.Location == null ? null : new { a.Location.Id, a.Location.Name },
            Company = a.Company == null ? null : new { a.Company.Id, a.Company.Name }
        }});
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

    // ==================== UPDATE (Direct — same as before, logs via centralized service through SaveChanges) ====================

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "accessories.edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAccessoryRequest r)
    {
        var a = await _context.Accessories.FindAsync(id);
        if (a == null) return NotFound(new { status = "error", message = "Accessory not found." });

        // Company scoping: a regular user may only edit accessories of their own company (or floater).
        var userCompanyId = await GetUserCompanyIdAsync();
        if (userCompanyId.HasValue && a.CompanyId.HasValue && a.CompanyId.Value != userCompanyId.Value)
            return NotFound(new { status = "error", message = "Accessory not found." });

        // CompanyId-lock after any checkout history (mirrors Consumable/License): past checkouts were
        // tied to the old company. Patch-aware — only when CompanyId is explicitly sent and differs.
        if (r.CompanyId.HasValue && r.CompanyId.Value != a.CompanyId
            && await _context.AccessoryCheckouts.AnyAsync(ch => ch.AccessoryId == id))
            return BadRequest(new { status = "error", message = "Phụ kiện đã từng được cấp phát — không thể đổi công ty.", error_code = "FIELD_LOCKED" });

        var hasActiveCheckouts = await _context.AccessoryCheckouts.AnyAsync(ch => ch.AccessoryId == id && ch.AssignedQty > ch.ReturnedQty);
        if (hasActiveCheckouts)
            return BadRequest(new { status = "error", message = "Không thể sửa phụ kiện đang có thiết bị đang được cấp phát." });

        // Task M2 patch semantics: only fields explicitly sent are applied.
        if (!string.IsNullOrWhiteSpace(r.Name)) a.Name = r.Name;
        if (r.ItemNo is not null) a.ItemNo = r.ItemNo;
        if (r.Qty.HasValue) a.Qty = r.Qty.Value;
        if (r.MinAmt.HasValue) a.MinAmt = r.MinAmt.Value;
        if (r.CategoryId is not null) a.CategoryId = r.CategoryId;
        if (r.ManufacturerId is not null) a.ManufacturerId = r.ManufacturerId;
        if (r.SupplierId is not null) a.SupplierId = r.SupplierId;
        if (r.LocationId is not null) a.LocationId = r.LocationId;
        if (r.CompanyId.HasValue) a.CompanyId = r.CompanyId.Value;
        if (r.ModelNumber is not null) a.ModelNumber = r.ModelNumber;
        if (r.OrderNumber is not null) a.OrderNumber = r.OrderNumber;
        if (r.PurchaseCost is not null) a.PurchaseCost = r.PurchaseCost;
        if (r.PurchaseDate is not null) a.PurchaseDate = r.PurchaseDate;
        if (r.Notes is not null) a.Notes = r.Notes;
        if (r.Image is not null) a.Image = r.Image;
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Accessory updated." });
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