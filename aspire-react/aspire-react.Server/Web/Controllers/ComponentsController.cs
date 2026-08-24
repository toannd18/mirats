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
[Route("api/v1/components")]
public class ComponentsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IComponentAllocationService _allocationService;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public ComponentsController(AppDbContext context, IComponentAllocationService allocationService, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _allocationService = allocationService;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    private Task<Guid?> GetUserCompanyIdAsync() => _companyScope.GetCurrentUserCompanyIdAsync();

    [HttpGet]
    [Authorize(Policy = "components.view")]
    public async Task<IActionResult> GetComponents([FromQuery] string? search, [FromQuery] Guid? categoryId,
        [FromQuery] Guid? companyId, [FromQuery] Guid? locationId,
        [FromQuery] bool uncategorized = false, [FromQuery] bool uncompanied = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var query = _context.Components
            .Include(c => c.Assignments)
            .Include(c => c.Units)
            .Include(c => c.Category)
            .Include(c => c.Company)
            .Include(c => c.Location)
            .Include(c => c.Supplier)
            .Include(c => c.Manufacturer)
            .AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(s) || (c.Serial != null && c.Serial.ToLower().Contains(s)));
        }
        if (uncategorized) query = query.Where(c => c.CategoryId == null);
        else if (categoryId.HasValue) query = query.Where(c => c.CategoryId == categoryId);
        if (uncompanied) query = query.Where(c => c.CompanyId == null);
        else if (companyId.HasValue) query = query.Where(c => c.CompanyId == companyId);
        if (locationId.HasValue) query = query.Where(c => c.LocationId == locationId);

        var userCompanyId = await GetUserCompanyIdAsync();
        query = query.Where(c => userCompanyId == null || c.CompanyId == null || c.CompanyId == userCompanyId.Value);

        var total = await query.CountAsync();
        var items = await query.OrderBy(c => c.Name).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Serial,
                c.Qty,
                c.MinAmt,
                c.ModelNumber,
                c.OrderNumber,
                c.PurchaseCost,
                c.PurchaseDate,
                c.Notes,
                TrackingType = c.TrackingType.ToString(),
                Remaining = c.TrackingType == TrackingType.Serial
                    ? c.Units.Count(u => u.Status == ComponentUnitStatus.InStock)
                    : c.Qty - c.Assignments.Sum(a => a.AssignedQty),
                IsLowStock = (c.TrackingType == TrackingType.Serial
                    ? c.Units.Count(u => u.Status == ComponentUnitStatus.InStock)
                    : c.Qty - c.Assignments.Sum(a => a.AssignedQty)) <= c.MinAmt,
                Category = c.Category == null ? null : new { c.Category.Id, c.Category.Name },
                Company = c.Company == null ? null : new { c.Company.Id, c.Company.Name },
                Location = c.Location == null ? null : new { c.Location.Id, c.Location.Name },
                Supplier = c.Supplier == null ? null : new { c.Supplier.Id, c.Supplier.Name },
                Manufacturer = c.Manufacturer == null ? null : new { c.Manufacturer.Id, c.Manufacturer.Name }
            }).ToListAsync();

        // canDelete = the component (or any of its serial units) has NEVER been checked out.
        var pageIds = items.Select(i => i.Id).ToList();
        var unitToComponent = await _context.ComponentUnits.IgnoreQueryFilters()
            .Where(u => pageIds.Contains(u.ComponentId))
            .Select(u => new { u.Id, u.ComponentId })
            .ToDictionaryAsync(u => u.Id, u => u.ComponentId);
        var hasHistory = new HashSet<Guid>();
        if (pageIds.Count > 0)
        {
            var checkoutLogs = await _context.ActionLogs.AsNoTracking()
                .Where(l => l.ActionType == ActionType.Checkout &&
                    ((l.ItemType == ItemType.Component && pageIds.Contains(l.ItemId)) ||
                     (l.ItemType == ItemType.ComponentUnit && unitToComponent.Keys.Contains(l.ItemId))))
                .Select(l => new { l.ItemType, l.ItemId })
                .ToListAsync();
            foreach (var log in checkoutLogs)
            {
                if (log.ItemType == ItemType.Component) hasHistory.Add(log.ItemId);
                else if (unitToComponent.TryGetValue(log.ItemId, out var cid)) hasHistory.Add(cid);
            }
        }

        var result = items.Select(c => new
        {
            c.Id,
            c.Name,
            c.Serial,
            c.Qty,
            c.MinAmt,
            c.ModelNumber,
            c.OrderNumber,
            c.PurchaseCost,
            c.PurchaseDate,
            c.Notes,
            c.TrackingType,
            c.Remaining,
            c.IsLowStock,
            canDelete = !hasHistory.Contains(c.Id),
            c.Category,
            c.Company,
            c.Location,
            c.Supplier,
            c.Manufacturer
        }).ToList();

        return Ok(new { status = "success", data = result, pagination = new { page, pageSize, totalItems = total, totalPages = (int)Math.Ceiling((double)total / pageSize), hasNextPage = page * pageSize < total, hasPreviousPage = page > 1 } });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "components.view")]
    public async Task<IActionResult> GetComponent(Guid id)
    {
        var userCompanyId = await GetUserCompanyIdAsync();
        var c = await _context.Components
            .Include(x => x.Assignments).ThenInclude(a => a.Asset)
            .Include(x => x.Units).ThenInclude(u => u.CurrentAsset)
            .Include(x => x.Category).Include(x => x.Location)
            .Include(x => x.Company).Include(x => x.Supplier).Include(x => x.Manufacturer)
            .AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (c == null || (userCompanyId.HasValue && c.CompanyId.HasValue && c.CompanyId.Value != userCompanyId.Value))
            return NotFound(new { status = "error", message = "Component not found." });

        var inStock = c.TrackingType == TrackingType.Serial
            ? c.Units.Count(u => u.Status == ComponentUnitStatus.InStock)
            : c.Qty - c.Assignments.Sum(a => a.AssignedQty);
        var allocated = c.TrackingType == TrackingType.Serial
            ? c.Units.Count(u => u.Status == ComponentUnitStatus.Allocated)
            : c.Assignments.Sum(a => a.AssignedQty);
        var damaged = c.TrackingType == TrackingType.Serial ? c.Units.Count(u => u.Status == ComponentUnitStatus.Damaged) : 0;
        var disposed = c.TrackingType == TrackingType.Serial ? c.Units.Count(u => u.Status == ComponentUnitStatus.Disposed) : 0;

        // canDelete = the component (or any of its serial units) has NEVER been checked out.
        var unitIds = c.Units.Select(u => u.Id).ToList();
        var hasCheckout =
            await _context.ActionLogs.AsNoTracking().AnyAsync(l => l.ActionType == ActionType.Checkout &&
                ((l.ItemType == ItemType.Component && l.ItemId == id) ||
                 (l.ItemType == ItemType.ComponentUnit && unitIds.Contains(l.ItemId))));

        return Ok(new
        {
            status = "success",
            data = new
            {
                c.Id,
                c.Name,
                c.Serial,
                c.ItemNo,
                c.Qty,
                c.MinAmt,
                c.ModelNumber,
                c.OrderNumber,
                c.PurchaseCost,
                c.PurchaseDate,
                c.Notes,
                c.UpdatedAt,
                TrackingType = c.TrackingType.ToString(),
                Remaining = inStock,
                IsLowStock = inStock <= c.MinAmt,
                UnitsSummary = new { inStock, allocated, damaged, disposed },
                canDelete = !hasCheckout,
                Category = c.Category == null ? null : new { c.Category.Id, c.Category.Name },
                Company = c.Company == null ? null : new { c.Company.Id, c.Company.Name },
                Location = c.Location == null ? null : new { c.Location.Id, c.Location.Name },
                Supplier = c.Supplier == null ? null : new { c.Supplier.Id, c.Supplier.Name },
                Manufacturer = c.Manufacturer == null ? null : new { c.Manufacturer.Id, c.Manufacturer.Name },
                Assignments = c.Assignments.Select(a => new { a.Id, a.AssignedQty, a.Note, Asset = new { a.Asset.Id, a.Asset.AssetTag, a.Asset.Name } }),
                Units = c.Units.Select(u => new
                {
                    u.Id,
                    u.SerialNo,
                    Status = u.Status.ToString(),
                    u.CurrentAssetId,
                    u.Notes,
                    u.CreatedAt,
                    u.UpdatedAt,
                    CurrentAsset = u.CurrentAsset == null ? null : new { u.CurrentAsset.Id, u.CurrentAsset.AssetTag, u.CurrentAsset.Name }
                })
            }
        });
    }

    [HttpPost]
    [Authorize(Policy = "components.create")]
    public async Task<IActionResult> Create([FromBody] CreateComponentRequest r)
    {
        // [Task L2] Company-scoping on CREATE: a regular user may only create components for their
        // own company (or company-less floater). Superuser (scope → null) may create for any company.
        var userCompanyId = await GetUserCompanyIdAsync();
        if (userCompanyId.HasValue && r.CompanyId.HasValue && r.CompanyId.Value != userCompanyId.Value)
            return BadRequest(new { status = "error", message = "Bạn chỉ được tạo linh kiện cho công ty của mình.", error_code = "COMPANY_MISMATCH" });

        if (r.TrackingType == TrackingType.Serial && r.Qty.HasValue)
            return BadRequest(new { status = "error", message = "Không gửi qty khi tạo linh kiện Serial — số lượng được suy ra từ danh sách serial." });
        if (r.TrackingType == TrackingType.Bulk && (!r.Qty.HasValue || r.Qty.Value <= 0))
            return BadRequest(new { status = "error", message = "Qty bắt buộc (>0) cho linh kiện Bulk." });
        if (!r.CategoryId.HasValue)
            return BadRequest(new { status = "error", message = "Danh mục (Category) là bắt buộc khi tạo linh kiện.", error_code = "CATEGORY_REQUIRED" });
        var categoryExists = await _context.Categories.AnyAsync(c => c.Id == r.CategoryId.Value && c.CategoryType == CategoryType.Component);
        if (!categoryExists)
            return BadRequest(new { status = "error", message = "Danh mục không hợp lệ (phải thuộc loại Component).", error_code = "INVALID_CATEGORY" });
        if (!r.CompanyId.HasValue)
            return BadRequest(new { status = "error", message = "Công ty (Company) là bắt buộc khi tạo linh kiện.", error_code = "COMPANY_REQUIRED" });
        if (!await _context.Companies.AnyAsync(c => c.Id == r.CompanyId.Value))
            return BadRequest(new { status = "error", message = "Công ty không hợp lệ.", error_code = "INVALID_COMPANY" });

        var userId = GetCurrentUserId();

        // Npgsql retrying execution strategy requires transactions to run inside CreateExecutionStrategy.
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync();

            var component = new Component
            {
                Name = r.Name,
                Serial = r.Serial,
                Qty = 0,
                MinAmt = r.MinAmt,
                TrackingType = r.TrackingType,
                CategoryId = r.CategoryId,
                LocationId = r.LocationId,
                CompanyId = r.CompanyId,
                SupplierId = r.SupplierId,
                ManufacturerId = r.ManufacturerId,
                ModelNumber = r.ModelNumber,
                OrderNumber = r.OrderNumber,
                PurchaseDate = r.PurchaseDate,
                PurchaseCost = r.PurchaseCost,
                Notes = r.Notes
            };
            _context.Components.Add(component);
            _actionLogService.Log(new ActionLogEntry
            {
                ItemType = ItemType.Component,
                ItemId = component.Id,
                ActionType = ActionType.Create,
                CreatedBy = userId,
                CompanyId = component.CompanyId,
                Note = $"Tạo linh kiện (TrackingType: {r.TrackingType})"
            });

            if (r.TrackingType == TrackingType.Bulk)
            {
                component.Qty = r.Qty!.Value;
            }
            else if (r.SerialNumbers?.Any() == true)
            {
                // Initial serial stock — reuse the StockIn path so serial validation + per-unit audit logging
                // stay in one place. Component + units + logs all commit in the same transaction.
                await _context.SaveChangesAsync();
                var result = await _allocationService.StockInAsync(component.Id, r.SerialNumbers, "Nhập kho ban đầu khi tạo", userId);
                if (!result.Success)
                {
                    await tx.RollbackAsync();
                    return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
                }
            }

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            return CreatedAtAction(nameof(GetComponent), new { id = component.Id },
                new { status = "success", message = "Component created.", data = new { component.Id, component.Name, component.Qty, TrackingType = component.TrackingType.ToString() } });
        });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "components.edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateComponentRequest r)
    {
        var c = await _context.Components.FindAsync(id);
        if (c == null) return NotFound(new { status = "error", message = "Component not found." });

        // Company scoping: a regular user may only edit components of their own company (or floater).
        var userCompanyId = await GetUserCompanyIdAsync();
        if (userCompanyId.HasValue && c.CompanyId.HasValue && c.CompanyId.Value != userCompanyId.Value)
            return NotFound(new { status = "error", message = "Component not found." });

        // ─── Locked fields: cannot be changed via update ───
        // Reject when the payload carries a value DIFFERENT from the current one so the user
        // knows these fields are immutable (not silently ignored). Sending the same value is fine.
        var locked = new List<string>();
        if (r.CategoryId.HasValue && r.CategoryId.Value != c.CategoryId) locked.Add("categoryId");
        if (r.CompanyId.HasValue && r.CompanyId.Value != c.CompanyId) locked.Add("companyId");
        if (r.TrackingType.HasValue && r.TrackingType.Value != c.TrackingType) locked.Add("trackingType");
        if (locked.Count > 0)
            return BadRequest(new
            {
                status = "error",
                message = $"Không thể thay đổi (field đã khóa): {string.Join(", ", locked)}. Tracking type, Category và Company chỉ xác định lúc tạo.",
                error_code = "FIELD_LOCKED"
            });

        // ─── Patch semantics (Task M1, mirroring Task F Asset): only fields EXPLICITLY sent
        // (non-null) are applied. A partial payload (e.g. only Name/Notes) must NOT wipe the other
        // fields back to null/empty. Qty/Serial/ItemNo stay silently ignored (Qty is read-only).
        if (!string.IsNullOrWhiteSpace(r.Name)) c.Name = r.Name;
        c.Notes = r.Notes ?? c.Notes;
        if (r.SupplierId is not null) c.SupplierId = r.SupplierId;
        if (r.ManufacturerId is not null) c.ManufacturerId = r.ManufacturerId;
        if (r.ModelNumber is not null) c.ModelNumber = r.ModelNumber;
        if (r.MinAmt.HasValue) c.MinAmt = r.MinAmt.Value;
        if (r.LocationId is not null) c.LocationId = r.LocationId;
        if (r.OrderNumber is not null) c.OrderNumber = r.OrderNumber;
        if (r.PurchaseCost is not null) c.PurchaseCost = r.PurchaseCost;
        if (r.PurchaseDate is not null) c.PurchaseDate = r.PurchaseDate;

        _actionLogService.Log(new ActionLogEntry { ItemType = ItemType.Component, ItemId = id, ActionType = ActionType.Update, CreatedBy = GetCurrentUserId(), CompanyId = c.CompanyId, Note = $"Cập nhật linh kiện \"{c.Name}\"" });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Component updated." });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "components.delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var c = await _context.Components.Include(x => x.Units).FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound(new { status = "error", message = "Component not found." });

        // Company scoping: a regular user may only delete components of their own company (or floater).
        var userCompanyId = await GetUserCompanyIdAsync();
        if (userCompanyId.HasValue && c.CompanyId.HasValue && c.CompanyId.Value != userCompanyId.Value)
            return NotFound(new { status = "error", message = "Component not found." });

        // ─── Delete guard ───
        // A component that has EVER been checked out (Component-level or any of its serial units)
        // cannot be deleted — the ActionLog audit trail must stay intact. Even if everything has
        // been checked back in, history is preserved for auditing.
        var unitIds = c.Units.Select(u => u.Id).ToList();
        var hasAllocationHistory =
            await _context.ActionLogs.AsNoTracking().AnyAsync(l => l.ActionType == ActionType.Checkout &&
                ((l.ItemType == ItemType.Component && l.ItemId == id) ||
                 (l.ItemType == ItemType.ComponentUnit && unitIds.Contains(l.ItemId))));
        if (hasAllocationHistory)
            return BadRequest(new { status = "error", message = "Linh kiện đã từng được cấp phát, không thể xóa.", error_code = "COMPONENT_HAS_ALLOCATION_HISTORY" });

        _actionLogService.Log(new ActionLogEntry { ItemType = ItemType.Component, ItemId = id, ActionType = ActionType.Delete, CreatedBy = GetCurrentUserId(), CompanyId = c.CompanyId, Note = $"Xóa linh kiện \"{c.Name}\"" });
        _context.Components.Remove(c);
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Component deleted." });
    }

    // ==================== Legacy quantity endpoints (kept for backward compatibility) ====================

    [HttpPost("{id:guid}/assign")]
    [Authorize(Policy = "components.checkout")]
    public Task<IActionResult> Assign(Guid id, [FromBody] AssignComponentRequest r)
        => RunTransactional((userId, ct) => _allocationService.AllocateAsync(id, r.AssetId, r.AssignedQty, null, r.Note, userId, ct));

    [HttpPost("{id:guid}/remove")]
    [Authorize(Policy = "components.checkout")]
    public async Task<IActionResult> RemoveAssignment(Guid id, [FromBody] RemoveComponentRequest r)
    {
        // [Task K] Company-scoping: only a user of the component's company may remove its assignment.
        var userCompanyId = await GetUserCompanyIdAsync();
        var a = await _context.ComponentAssignments.Include(ca => ca.Component).FirstOrDefaultAsync(ca => ca.Id == r.AssignmentId && ca.ComponentId == id);
        if (a == null || (userCompanyId.HasValue && a.Component.CompanyId.HasValue && a.Component.CompanyId.Value != userCompanyId.Value))
            return NotFound(new { status = "error", message = "Assignment not found." });
        if (a.Component.TrackingType == TrackingType.Serial)
            return BadRequest(new { status = "error", message = "Linh kiện Serial không dùng assignment quantity — dùng /checkin." });
        _context.ComponentAssignments.Remove(a);

        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.Component,
            ItemId = id,
            TargetType = AssignmentTargetType.Asset,
            TargetId = a.AssetId,
            ActionType = ActionType.Checkin,
            CreatedBy = GetCurrentUserId(),
            CompanyId = a.Component.CompanyId,
            LogMeta = JsonSerializer.Serialize(new { quantity = a.AssignedQty })
        });

        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Component assignment removed." });
    }

    // ==================== Serial & Bulk unified endpoints ====================

    [HttpGet("{id:guid}/units")]
    [Authorize(Policy = "components.view")]
    public async Task<IActionResult> GetUnits(Guid id, [FromQuery] ComponentUnitStatus? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        // Company scoping: verify the parent component is visible to the current user.
        var userCompanyId = await GetUserCompanyIdAsync();
        var visible = await _context.Components.AsNoTracking()
            .AnyAsync(c => c.Id == id && (userCompanyId == null || c.CompanyId == null || c.CompanyId == userCompanyId.Value));
        if (!visible) return NotFound(new { status = "error", message = "Component not found." });

        var query = _context.ComponentUnits
            .Include(u => u.CurrentAsset)
            .Where(u => u.ComponentId == id);
        if (status.HasValue) query = query.Where(u => u.Status == status.Value);

        var total = await query.CountAsync();
        var units = await query.OrderBy(u => u.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(u => new
            {
                u.Id,
                u.SerialNo,
                Status = u.Status.ToString(),
                u.CurrentAssetId,
                u.Notes,
                u.CreatedAt,
                u.UpdatedAt,
                CurrentAsset = u.CurrentAsset == null ? null : new { u.CurrentAsset.Id, u.CurrentAsset.AssetTag, u.CurrentAsset.Name }
            }).ToListAsync();

        // canDelete = the unit has NEVER been checked out (audit history must stay intact).
        var pageUnitIds = units.Select(u => u.Id).ToList();
        var blockedUnits = new HashSet<Guid>();
        if (pageUnitIds.Count > 0)
        {
            blockedUnits = (await _context.ActionLogs.AsNoTracking()
                .Where(l => l.ItemType == ItemType.ComponentUnit && l.ActionType == ActionType.Checkout && pageUnitIds.Contains(l.ItemId))
                .Select(l => l.ItemId).Distinct().ToListAsync()).ToHashSet();
        }
        var result = units.Select(u => new { u.Id, u.SerialNo, u.Status, u.CurrentAssetId, u.Notes, u.CreatedAt, u.UpdatedAt, u.CurrentAsset, canDelete = !blockedUnits.Contains(u.Id) }).ToList();
        return Ok(new { status = "success", data = result, pagination = new { page, pageSize, totalItems = total, totalPages = (int)Math.Ceiling((double)total / pageSize), hasNextPage = page * pageSize < total, hasPreviousPage = page > 1 } });
    }

    [HttpPost("{id:guid}/units")]
    [Authorize(Policy = "components.edit")]
    public Task<IActionResult> StockInUnits(Guid id, [FromBody] StockInUnitsRequest r)
        => RunTransactional((userId, ct) => _allocationService.StockInAsync(id, r.SerialNumbers, r.Note, userId, ct));

    [HttpPost("{id:guid}/checkout")]
    [Authorize(Policy = "components.checkout")]
    public Task<IActionResult> Checkout(Guid id, [FromBody] CheckoutComponentRequest r)
        => RunTransactional((userId, ct) => _allocationService.AllocateAsync(id, r.AssetId, r.Quantity, r.SerialNo, r.Note, userId, ct));

    [HttpPost("{id:guid}/checkin")]
    [Authorize(Policy = "components.checkout")]
    public Task<IActionResult> Checkin(Guid id, [FromBody] CheckinComponentRequest r)
        => RunTransactional((userId, ct) => _allocationService.ReturnAsync(id, r.AssetId, r.Quantity, r.SerialNo, r.Note, userId, ct));

    /// <summary>Component-level history + history of every ComponentUnit belonging to it.</summary>
    [HttpGet("{id:guid}/action-logs")]
    [Authorize(Policy = "components.view")]
    public async Task<IActionResult> GetActionLogs(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        // Company scoping: verify the parent component is visible to the current user.
        var userCompanyId = await GetUserCompanyIdAsync();
        var visible = await _context.Components.AsNoTracking()
            .AnyAsync(c => c.Id == id && (userCompanyId == null || c.CompanyId == null || c.CompanyId == userCompanyId.Value));
        if (!visible) return NotFound(new { status = "error", message = "Component not found." });

        var unitIds = await _context.ComponentUnits.IgnoreQueryFilters()
            .Where(u => u.ComponentId == id).Select(u => u.Id).ToListAsync();

        var query = _context.ActionLogs.Include(l => l.Creator).AsNoTracking()
            .Where(l => (l.ItemType == ItemType.Component && l.ItemId == id)
                        || (l.ItemType == ItemType.ComponentUnit && unitIds.Contains(l.ItemId)))
            .OrderByDescending(l => l.ActionDate);

        var total = await query.CountAsync();
        var logs = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(l => new
            {
                l.Id,
                ItemType = l.ItemType.ToString(),
                l.ItemId,
                ActionType = l.ActionType.ToString(),
                ActionTypeValue = (int)l.ActionType,
                TargetType = l.TargetType.HasValue ? l.TargetType.Value.ToString() : null,
                l.TargetId,
                CreatorName = l.Creator != null
                    ? (l.Creator.FirstName + " " + l.Creator.LastName).Trim() != "" ? (l.Creator.FirstName + " " + l.Creator.LastName).Trim() : l.Creator.Username
                    : null,
                l.Note,
                l.LogMeta,
                l.ActionDate,
                l.LocationName,
                l.TargetSystemInfoName
            }).ToListAsync();

        // Batch-resolve target names (assets) — same mechanism as /action-logs.
        var targetIds = logs.Where(x => x.TargetId.HasValue).Select(x => x.TargetId!.Value).Distinct().ToList();
        var assetNames = targetIds.Count > 0
            ? await _context.Assets.Where(a => targetIds.Contains(a.Id))
                .Select(a => new { a.Id, Name = a.AssetTag + " - " + a.Name })
                .ToDictionaryAsync(a => a.Id, a => a.Name)
            : new Dictionary<Guid, string>();

        var enriched = logs.Select(log => new
        {
            log.Id,
            log.ItemType,
            log.ItemId,
            log.ActionType,
            log.ActionTypeValue,
            log.TargetType,
            log.TargetId,
            TargetName = log.TargetId.HasValue ? assetNames.GetValueOrDefault(log.TargetId.Value) : null,
            log.CreatorName,
            log.Note,
            log.LogMeta,
            log.ActionDate,
            log.LocationName,
            log.TargetSystemInfoName
        }).ToList();

        return Ok(new { status = "success", data = enriched, total });
    }

    /// <summary>
    /// Runs an allocation-service operation inside a transaction so the domain change + its
    /// ActionLog commit (or roll back) together. The service writes both via the same SaveChanges.
    /// Npgsql's retrying execution strategy requires the transaction to run inside CreateExecutionStrategy.
    /// </summary>
    private async Task<IActionResult> RunTransactional(Func<Guid, CancellationToken, Task<ComponentOperationResult>> operation)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            var result = await operation(GetCurrentUserId(), CancellationToken.None);
            if (!result.Success)
            {
                await tx.RollbackAsync();
                return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
            }
            await tx.CommitAsync();
            return Ok(new { status = "success", message = result.Message });
        });
    }

    private Guid GetCurrentUserId()
    {
        // [SEC-FIX CLAIM-CLEANUP, 2026-08-23] ONLY "local_user_id" (JIT-stamped). Keycloak
        // sub/preferred_username are never a user identity source (bug-class 1). Absent → Guid.Empty.
        if (Guid.TryParse(User.FindFirstValue("local_user_id"), out var local)) return local;
        return Guid.Empty;
    }
}

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