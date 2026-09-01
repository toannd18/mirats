using System.Text.Json;
using System.Security.Claims;
using aspire_react.Server.Application.Common;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Caching;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Web.Controllers;

[ApiController, Route("api/v1"), Authorize]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IActionLogService _actionLogService;
    public AdminController(AppDbContext context, ICacheInvalidator cacheInvalidator, IActionLogService actionLogService)
    {
        _context = context;
        _cacheInvalidator = cacheInvalidator;
        _actionLogService = actionLogService;
    }

    // === Models ===
    [HttpGet("models"), Authorize(Policy = "models.view")]
    public async Task<IActionResult> GetModels()
    {
        var list = await _context.Models.Include(m => m.Manufacturer).Include(m => m.Category).Include(m => m.Depreciation)
            .AsNoTracking().OrderBy(m => m.Name)
            .Select(m => new
            {
                m.Id,
                m.Name,
                m.ModelNumber,
                m.Eol,
                m.Notes,
                m.Requestable,
                m.ManufacturerId,
                m.CategoryId,
                m.DepreciationId,
                m.FieldsetId,
                Manufacturer = m.Manufacturer == null ? null : new { m.Manufacturer.Id, m.Manufacturer.Name },
                Category = m.Category == null ? null : new { m.Category.Id, m.Category.Name },
                Depreciation = m.Depreciation == null ? null : new { m.Depreciation.Id, m.Depreciation.Name, m.Depreciation.Months }
            }).ToListAsync();
        return Ok(new { status = "success", data = list });
    }
    [HttpPost("models"), Authorize(Policy = "models.create")]
    public async Task<IActionResult> CreateModel(AssetModel m)
    {
        _context.Models.Add(m); await _context.SaveChangesAsync();
        _actionLogService.Log(new ActionLogEntry { ItemType = ItemType.Model, ItemId = m.Id, ActionType = ActionType.Create, CreatedBy = GetCurrentUserId(), CompanyId = null, Note = $"Tạo model \"{m.Name}\"" });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", data = new { m.Id } });
    }
    [HttpPut("models/{id:guid}"), Authorize(Policy = "models.edit")]
    public async Task<IActionResult> UpdateModel(Guid id, [FromBody] UpdateAssetModelRequest updated)
    {
        var m = await _context.Models.FindAsync(id);
        if (m == null) return NotFound(new { status = "error", message = "Not found." });
        // Task M2 patch semantics: only fields explicitly sent are applied (absent → keep current).
        var before = new { m.Name, m.ModelNumber, m.ManufacturerId, m.CategoryId, m.DepreciationId, m.FieldsetId, m.Eol, m.Notes, m.Requestable };
        if (!string.IsNullOrWhiteSpace(updated.Name)) m.Name = updated.Name;
        if (updated.ModelNumber is not null) m.ModelNumber = updated.ModelNumber;
        if (updated.ManufacturerId is not null) m.ManufacturerId = updated.ManufacturerId;
        if (updated.CategoryId is not null) m.CategoryId = updated.CategoryId;
        if (updated.DepreciationId is not null) m.DepreciationId = updated.DepreciationId;
        if (updated.FieldsetId is not null) m.FieldsetId = updated.FieldsetId;
        if (updated.Eol is not null) m.Eol = updated.Eol;
        if (updated.Notes is not null) m.Notes = updated.Notes;
        if (updated.Requestable.HasValue) m.Requestable = updated.Requestable.Value;
        await _context.SaveChangesAsync();
        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.Model,
            ItemId = id,
            ActionType = ActionType.Update,
            CreatedBy = GetCurrentUserId(),
            CompanyId = null,
            LogMeta = JsonSerializer.Serialize(new { changes = new { name = new { old = before.Name, @new = m.Name }, modelNumber = new { old = before.ModelNumber, @new = m.ModelNumber }, manufacturerId = new { old = before.ManufacturerId, @new = m.ManufacturerId }, categoryId = new { old = before.CategoryId, @new = m.CategoryId }, depreciationId = new { old = before.DepreciationId, @new = m.DepreciationId }, fieldsetId = new { old = before.FieldsetId, @new = m.FieldsetId }, eol = new { old = before.Eol, @new = m.Eol }, notes = new { old = before.Notes, @new = m.Notes }, requestable = new { old = before.Requestable, @new = m.Requestable } } }),
            Note = $"Cập nhật model \"{m.Name}\""
        });
        return Ok(new { status = "success", message = "Updated." });
    }
    [HttpDelete("models/{id:guid}"), Authorize(Policy = "models.delete")]
    public async Task<IActionResult> DeleteModel(Guid id)
    {
        var hasAssets = await _context.Assets.AnyAsync(a => a.ModelId == id);
        if (hasAssets) return BadRequest(new { status = "error", message = "Không thể xóa Model đang có tài sản sử dụng." });
        var m = await _context.Models.FindAsync(id);
        if (m == null) return NotFound(new { status = "error", message = "Not found." });
        var nameM = m.Name;
        _context.Models.Remove(m); await _context.SaveChangesAsync();
        _actionLogService.Log(new ActionLogEntry { ItemType = ItemType.Model, ItemId = id, ActionType = ActionType.Delete, CreatedBy = GetCurrentUserId(), CompanyId = null, Note = $"Xóa model \"{nameM}\"" });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Deleted." });
    }

    // === Categories ===
    // [Giai đoạn 2] Category CRUD extracted to CategoriesController (standalone, MediatR) —
    // routes unchanged: /api/v1/categories... See docs/MEDIATR_MIGRATION_PLAYBOOK.md §6.

    // === Manufacturers ===
    // [Giai đoạn 2] Manufacturer CRUD extracted to ManufacturersController (standalone, MediatR) —
    // routes unchanged: /api/v1/manufacturers...

    // === Suppliers ===
    // [Giai đoạn 2] Supplier CRUD extracted to SuppliersController (standalone, MediatR) —
    // routes unchanged: /api/v1/suppliers...

    // === Locations ===
    // [Giai đoạn 2] Location CRUD extracted to LocationsController (standalone, MediatR) —
    // routes unchanged: /api/v1/locations... Create's missing company-scoping = BUG-G (BACKLOG.md).

    // === Status Labels ===
    [HttpGet("statuslabels"), Authorize(Policy = "statuslabels.view")]
    public async Task<IActionResult> GetStatusLabels()
    {
        var list = await _context.StatusLabels.AsNoTracking().OrderBy(s => s.Name).ToListAsync();
        return Ok(new { status = "success", data = list });
    }

    // === Depreciations ===
    // T-CLEAN1: trước đây chỉ [Authorize] trần (review #33 BACKEND_ARCHITECTURE_REVIEW_2026-08-15) —
    // mọi user đăng nhập đều đọc được. Siết về policy chuẩn như các master-data khác.
    [HttpGet("depreciations"), Authorize(Policy = "depreciations.view")]
    public async Task<IActionResult> GetDepreciations()
    {
        var list = await _context.Depreciations.AsNoTracking().OrderBy(d => d.Name).ToListAsync();
        return Ok(new { status = "success", data = list });
    }

    private Guid GetCurrentUserId()
    {
        // [SEC-FIX CLAIM-CLEANUP, 2026-08-23] ONLY "local_user_id" (JIT-stamped). Keycloak
        // sub/preferred_username are never a user identity source (bug-class 1). Absent → Guid.Empty.
        if (Guid.TryParse(User.FindFirstValue("local_user_id"), out var local)) return local;
        return Guid.Empty;
    }
}

/// <summary>Patch-style Update DTO for AssetModel (Task M2) — nullable so a partial payload only changes sent fields.</summary>
public record UpdateAssetModelRequest(
    string? Name = null, string? ModelNumber = null, Guid? ManufacturerId = null, Guid? CategoryId = null,
    Guid? DepreciationId = null, Guid? FieldsetId = null, int? Eol = null, string? Notes = null, bool? Requestable = null);

// UpdateCategoryRequest moved to CategoriesController.cs (Giai đoạn 2 — Category section extraction).