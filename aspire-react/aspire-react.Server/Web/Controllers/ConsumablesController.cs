using System.Security.Claims;
using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Web.Controllers;

[ApiController]
[Route("api/v1/consumables")]
public class ConsumablesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IActionLogService _actionLogService;
    private readonly IConsumableAllocationService _allocationService;
    private readonly ICompanyScopeService _companyScope;
    public ConsumablesController(AppDbContext context, IActionLogService actionLogService,
        IConsumableAllocationService allocationService, ICompanyScopeService companyScope)
    {
        _context = context;
        _actionLogService = actionLogService;
        _allocationService = allocationService;
        _companyScope = companyScope;
    }

    private Task<Guid?> GetUserCompanyIdAsync() => _companyScope.GetCurrentUserCompanyIdAsync();

    [HttpGet]
    [Authorize(Policy = "consumables.view")]
    public async Task<IActionResult> GetConsumables([FromQuery] string? search, [FromQuery] Guid? categoryId,
        [FromQuery] Guid? locationId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var query = _context.Consumables.Include(c => c.Checkouts).Include(c => c.Category)
            .Include(c => c.Location).Include(c => c.Company).AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(s) || (c.ItemNo != null && c.ItemNo.ToLower().Contains(s)));
        }
        if (categoryId.HasValue) query = query.Where(c => c.CategoryId == categoryId);
        if (locationId.HasValue) query = query.Where(c => c.LocationId == locationId);

        var userCompanyId = await GetUserCompanyIdAsync();
        query = query.Where(c => userCompanyId == null || c.CompanyId == null || c.CompanyId == userCompanyId.Value);

        var total = await query.CountAsync();
        var items = await query.OrderBy(c => c.Name).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new {
                c.Id, c.Name, c.ItemNo, c.Notes, c.Qty, c.MinAmt, c.Status,
                c.CompanyId,
                CompanyName = c.Company != null ? c.Company.Name : null,
                Remaining = c.Qty - c.Checkouts.Sum(ch => ch.Quantity),
                IsLowStock = (c.Qty - c.Checkouts.Sum(ch => ch.Quantity)) <= c.MinAmt,
                Category = c.Category == null ? null : new { c.Category.Id, c.Category.Name },
                Location = c.Location == null ? null : new { c.Location.Id, c.Location.Name }
            }).ToListAsync();

        return Ok(new { status = "success", data = items, pagination = new { page, pageSize, totalItems = total, totalPages = (int)Math.Ceiling((double)total / pageSize), hasNextPage = page * pageSize < total, hasPreviousPage = page > 1 } });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "consumables.view")]
    public async Task<IActionResult> GetConsumable(Guid id)
    {
        var c = await _context.Consumables.Include(x => x.Checkouts).Include(x => x.Category)
            .Include(x => x.Manufacturer).Include(x => x.Supplier).Include(x => x.Location)
            .Include(x => x.Company).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound(new { status = "error", message = "Consumable not found." });

        var userCompanyId = await GetUserCompanyIdAsync();
        if (userCompanyId.HasValue && c.CompanyId.HasValue && c.CompanyId.Value != userCompanyId.Value)
            return NotFound(new { status = "error", message = "Consumable not found." });

        var remaining = c.Qty - c.Checkouts.Sum(ch => ch.Quantity);
        return Ok(new { status = "success", data = new {
            c.Id, c.Name, c.ItemNo, c.Qty, c.MinAmt, Status = c.Status.ToString(),
            c.ModelNumber, c.OrderNumber, c.PurchaseDate, c.PurchaseCost, c.Notes,
            c.CategoryId, c.ManufacturerId, c.SupplierId, c.LocationId, c.CompanyId,
            Remaining = remaining, PercentRemaining = c.Qty > 0 ? Math.Round((double)remaining / c.Qty * 100, 2) : 0,
            IsLowStock = remaining <= c.MinAmt,
            Category = c.Category == null ? null : new { c.Category.Id, c.Category.Name },
            Manufacturer = c.Manufacturer == null ? null : new { c.Manufacturer.Id, c.Manufacturer.Name },
            Supplier = c.Supplier == null ? null : new { c.Supplier.Id, c.Supplier.Name },
            Location = c.Location == null ? null : new { c.Location.Id, c.Location.Name },
            Company = c.Company == null ? null : new { c.Company.Id, c.Company.Name }
        }});
    }

    [HttpPost]
    [Authorize(Policy = "consumables.create")]
    public async Task<IActionResult> Create([FromBody] CreateConsumableRequest r)
    {
        // [Task L2] Company-scoping on CREATE: a regular user may only create consumables for their
        // own company (or company-less floater). Superuser (scope → null) may create for any company.
        var userCompanyId = await GetUserCompanyIdAsync();
        if (userCompanyId.HasValue && r.CompanyId.HasValue && r.CompanyId.Value != userCompanyId.Value)
            return BadRequest(new { status = "error", message = "Bạn chỉ được tạo vật tư cho công ty của mình.", error_code = "COMPANY_MISMATCH" });

        var c = new Consumable {
            Name = r.Name, ItemNo = r.ItemNo, Qty = r.Qty, MinAmt = r.MinAmt,
            CategoryId = r.CategoryId, ManufacturerId = r.ManufacturerId, SupplierId = r.SupplierId,
            LocationId = r.LocationId, CompanyId = r.CompanyId,
            ModelNumber = r.ModelNumber, OrderNumber = r.OrderNumber,
            PurchaseCost = r.PurchaseCost, PurchaseDate = r.PurchaseDate,
            Notes = r.Notes, Image = r.Image
        };
        _context.Consumables.Add(c);
        var createUserId = await GetCurrentUserIdAsync();
        _actionLogService.LogAction(
            itemType: ItemType.Consumable,
            itemId: c.Id,
            actionType: ActionType.Create,
            loggedByUserId: createUserId,
            note: $"Created consumable: {c.Name}",
            logMeta: JsonSerializer.Serialize(new { name = c.Name, qty = c.Qty, minAmt = c.MinAmt }),
            companyId: c.CompanyId);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetConsumable), new { id = c.Id }, new { status = "success", message = "Consumable created.", data = new { c.Id, c.Name } });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "consumables.edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateConsumableRequest r)
    {
        var c = await _context.Consumables.FindAsync(id);
        if (c == null) return NotFound(new { status = "error", message = "Consumable not found." });

        // Company scoping: a regular user may only edit consumables of their own company (or floater).
        var userCompanyId = await GetUserCompanyIdAsync();
        if (userCompanyId.HasValue && c.CompanyId.HasValue && c.CompanyId.Value != userCompanyId.Value)
            return NotFound(new { status = "error", message = "Consumable not found." });

        var updateUserId = await GetCurrentUserIdAsync();

        // ─── Confirmed consumables: ONLY Location + Notes remain editable (mirrors Task F Asset:
        // confirmed → only Name/Notes). Everything else is locked post-confirmation. Patch-aware:
        // sending the SAME value as current is allowed; only a DIFFERENT value on a locked field
        // is rejected (so the edit form can submit its loaded values untouched).
        if (c.Status == ConsumableStatus.Confirmed)
        {
            var locked = new List<string>();
            if (r.Name is not null && r.Name != c.Name) locked.Add("name");
            if (r.ItemNo is not null && r.ItemNo != c.ItemNo) locked.Add("itemNo");
            if (r.Qty.HasValue && r.Qty.Value != c.Qty) locked.Add("qty");
            if (r.MinAmt.HasValue && r.MinAmt.Value != c.MinAmt) locked.Add("minAmt");
            if (r.CategoryId is not null && r.CategoryId != c.CategoryId) locked.Add("categoryId");
            if (r.ManufacturerId is not null && r.ManufacturerId != c.ManufacturerId) locked.Add("manufacturerId");
            if (r.SupplierId is not null && r.SupplierId != c.SupplierId) locked.Add("supplierId");
            if (r.CompanyId.HasValue && r.CompanyId.Value != c.CompanyId) locked.Add("companyId");
            if (r.ModelNumber is not null && r.ModelNumber != c.ModelNumber) locked.Add("modelNumber");
            if (r.OrderNumber is not null && r.OrderNumber != c.OrderNumber) locked.Add("orderNumber");
            if (r.PurchaseCost.HasValue && r.PurchaseCost.Value != c.PurchaseCost) locked.Add("purchaseCost");
            if (r.PurchaseDate.HasValue && r.PurchaseDate.Value != c.PurchaseDate) locked.Add("purchaseDate");
            if (r.Image is not null && r.Image != c.Image) locked.Add("image");
            if (locked.Count > 0)
                return BadRequest(new { status = "error", message = $"Không thể sửa các trường: {string.Join(", ", locked)}. Vật tư đã xác nhận — chỉ Vị trí và Ghi chú được phép sửa.", error_code = "CONFIRMED_CONSUMABLE_LOCKED" });
            var oldLocationId = c.LocationId;
            var oldNotes = c.Notes;
            if (r.LocationId is not null) c.LocationId = r.LocationId;
            if (r.Notes is not null) c.Notes = r.Notes;
            _actionLogService.LogAction(
                itemType: ItemType.Consumable,
                itemId: id,
                actionType: ActionType.Update,
                loggedByUserId: updateUserId,
                note: $"Updated consumable: {c.Name}",
                logMeta: JsonSerializer.Serialize(new
                {
                    locationId = new { old = oldLocationId, @new = c.LocationId },
                    notes = new { old = oldNotes, @new = c.Notes }
                }),
                companyId: c.CompanyId);
        }
        else
        {
            // Field-lock company: a consumable that has ever been checked out cannot change company —
            // past checkouts were tied to the old company (mirrors License/Component's FIELD_LOCKED).
            // Patch-aware: only trigger when CompanyId is EXPLICITLY sent and differs.
            if (r.CompanyId.HasValue && r.CompanyId.Value != c.CompanyId
                && await _context.ConsumableCheckouts.AnyAsync(ch => ch.ConsumableId == id))
                return BadRequest(new { status = "error", message = "Vật tư đã từng được cấp phát — không thể đổi công ty.", error_code = "FIELD_LOCKED" });

            // ─── Patch semantics (Task M1, mirroring Task F Asset): only fields explicitly sent are applied.
            var oldName = c.Name;
            var oldQty = c.Qty;
            if (!string.IsNullOrWhiteSpace(r.Name)) c.Name = r.Name;
            if (r.ItemNo is not null) c.ItemNo = r.ItemNo;
            if (r.Qty.HasValue) c.Qty = r.Qty.Value;
            if (r.MinAmt.HasValue) c.MinAmt = r.MinAmt.Value;
            if (r.CategoryId is not null) c.CategoryId = r.CategoryId;
            if (r.ManufacturerId is not null) c.ManufacturerId = r.ManufacturerId;
            if (r.SupplierId is not null) c.SupplierId = r.SupplierId;
            if (r.LocationId is not null) c.LocationId = r.LocationId;
            if (r.CompanyId.HasValue) c.CompanyId = r.CompanyId.Value;
            if (r.ModelNumber is not null) c.ModelNumber = r.ModelNumber;
            if (r.OrderNumber is not null) c.OrderNumber = r.OrderNumber;
            if (r.PurchaseCost is not null) c.PurchaseCost = r.PurchaseCost;
            if (r.PurchaseDate is not null) c.PurchaseDate = r.PurchaseDate;
            if (r.Notes is not null) c.Notes = r.Notes;
            if (r.Image is not null) c.Image = r.Image;

            _actionLogService.LogAction(
                itemType: ItemType.Consumable,
                itemId: id,
                actionType: ActionType.Update,
                loggedByUserId: updateUserId,
                note: $"Updated consumable: {c.Name}",
                logMeta: JsonSerializer.Serialize(new
                {
                    name = new { old = oldName, @new = c.Name },
                    qty = new { old = oldQty, @new = c.Qty },
                    minAmt = new { old = c.MinAmt, @new = c.MinAmt }
                }),
                companyId: c.CompanyId);
        }
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Consumable updated." });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "consumables.delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var c = await _context.Consumables.FindAsync(id);
        if (c == null) return NotFound(new { status = "error", message = "Consumable not found." });

        // Company scoping: a regular user may only delete consumables of their own company (or floater).
        var userCompanyId = await GetUserCompanyIdAsync();
        if (userCompanyId.HasValue && c.CompanyId.HasValue && c.CompanyId.Value != userCompanyId.Value)
            return NotFound(new { status = "error", message = "Consumable not found." });

        if (c.Status == ConsumableStatus.Confirmed)
            return BadRequest(new { status = "error", message = "Không thể xóa vật tư đã được xác nhận." });
        // Delete guard: a consumable that has ever been checked out must keep its allocation history
        // (consumable_checkouts FK is CASCADE — hard-deleting would wipe the whole history).
        var hasCheckouts = await _context.ConsumableCheckouts.AnyAsync(ch => ch.ConsumableId == id);
        if (hasCheckouts)
            return BadRequest(new { status = "error", message = "Vật tư đã từng được cấp phát, không thể xóa (lịch sử cấp phát phải được giữ).", error_code = "CONSUMABLE_HAS_CHECKOUTS" });
        var deleteName = c.Name;
        var deleteCompanyId = c.CompanyId;
        _context.Consumables.Remove(c);
        var deleteUserId = await GetCurrentUserIdAsync();
        _actionLogService.LogAction(
            itemType: ItemType.Consumable,
            itemId: id,
            actionType: ActionType.Delete,
            loggedByUserId: deleteUserId,
            note: $"Deleted consumable: {deleteName}",
            companyId: deleteCompanyId);
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Consumable deleted." });
    }

    [HttpPut("{id:guid}/confirm")]
    [Authorize(Policy = "consumables.edit")]
    public async Task<IActionResult> Confirm(Guid id)
    {
        // [Task K] Company-scoping: only a user of the consumable's company may confirm it.
        var userCompanyId = await GetUserCompanyIdAsync();
        var c = await _context.Consumables.FindAsync(id);
        if (c == null || (userCompanyId.HasValue && c.CompanyId.HasValue && c.CompanyId.Value != userCompanyId.Value))
            return NotFound(new { status = "error", message = "Consumable not found." });
        if (c.Status == ConsumableStatus.Confirmed)
            return BadRequest(new { status = "error", message = "Vật tư đã được xác nhận." });
        c.Status = ConsumableStatus.Confirmed;
        var confirmUserId = await GetCurrentUserIdAsync();
        _actionLogService.LogAction(
            itemType: ItemType.Consumable,
            itemId: id,
            actionType: ActionType.Confirm,
            loggedByUserId: confirmUserId,
            note: "Consumable confirmed.",
            companyId: c.CompanyId);
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Consumable confirmed." });
    }

    [HttpPost("{id:guid}/checkout")]
    [Authorize(Policy = "consumables.checkout")]
    public Task<IActionResult> Checkout(Guid id, [FromBody] CheckoutConsumableRequest r)
        => RunTransactional(async (currentUserId, ct) =>
            await _allocationService.CheckoutAsync(id, r.UserId, r.Quantity, r.Note, currentUserId, ct));

    /// <summary>
    /// Runs a consumable allocation operation inside a transaction so the domain change + its
    /// ActionLog commit (or roll back) together. The service writes both via the same SaveChanges.
    /// Npgsql's retrying execution strategy requires the transaction to run inside CreateExecutionStrategy.
    /// </summary>
    private async Task<IActionResult> RunTransactional(Func<Guid, CancellationToken, Task<ConsumableCheckoutResult>> operation)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            var currentUserId = await GetCurrentUserIdAsync();
            var result = await operation(currentUserId, CancellationToken.None);
            if (!result.Success)
            {
                await tx.RollbackAsync();
                return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
            }
            await tx.CommitAsync();
            return Ok(new { status = "success", message = result.Message });
        });
    }

    [HttpGet("{id:guid}/checkouts")]
    [Authorize(Policy = "consumables.view")]
    public async Task<IActionResult> GetCheckouts(Guid id)
    {
        // Company scoping: a regular user may only view the checkouts of a consumable in their company.
        var userCompanyId = await GetUserCompanyIdAsync();
        var visible = await _context.Consumables.AsNoTracking()
            .AnyAsync(c => c.Id == id && (userCompanyId == null || c.CompanyId == null || c.CompanyId == userCompanyId.Value));
        if (!visible) return NotFound(new { status = "error", message = "Consumable not found." });

        var checkouts = await _context.ConsumableCheckouts
            .Include(ch => ch.User)
            .Include(ch => ch.CreatedByUser)
            .Where(ch => ch.ConsumableId == id)
            .OrderByDescending(ch => ch.CheckedOutAt)
            .Select(ch => new
            {
                ch.Id,
                ch.ConsumableId,
                ch.UserId,
                UserName = ch.User.Username,
                FirstName = ch.User.FirstName,
                LastName = ch.User.LastName,
                CreatedByName = ch.CreatedByUser != null ? ch.CreatedByUser.Username : null,
                CreatedByFirstName = ch.CreatedByUser != null ? ch.CreatedByUser.FirstName : null,
                CreatedByLastName = ch.CreatedByUser != null ? ch.CreatedByUser.LastName : null,
                ch.Quantity,
                ch.Note,
                CreatedAt = ch.CheckedOutAt
            })
            .ToListAsync();

        return Ok(new { status = "success", data = checkouts });
    }

    [HttpGet("low-stock")]
    [Authorize(Policy = "consumables.view")]
    public async Task<IActionResult> GetLowStock()
    {
        var userCompanyId = await GetUserCompanyIdAsync();
        var items = await _context.Consumables.Include(c => c.Checkouts).AsNoTracking()
            .Where(c => (userCompanyId == null || c.CompanyId == null || c.CompanyId == userCompanyId.Value)
                        && (c.Qty - c.Checkouts.Sum(ch => ch.Quantity)) <= c.MinAmt)
            .Select(c => new { c.Id, c.Name, c.ItemNo, c.Qty, c.MinAmt, Remaining = c.Qty - c.Checkouts.Sum(ch => ch.Quantity) })
            .ToListAsync();
        return Ok(new { status = "success", data = items });
    }

    private Task<Guid> GetCurrentUserIdAsync()
    {
        // [SEC-FIX CLAIM-CLEANUP, 2026-08-23] ONLY "local_user_id" (JIT-stamped) is a user identity
        // source — Keycloak sub/preferred_username are never used (bug-class 1). Absent → Guid.Empty.
        if (Guid.TryParse(User.FindFirstValue("local_user_id"), out var local)) return Task.FromResult(local);
        return Task.FromResult(Guid.Empty);
    }
}

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