using System.Text.Json;
using System.Security.Claims;
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
    private readonly ICompanyScopeService _companyScope;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IActionLogService _actionLogService;
    public AdminController(AppDbContext context, ICompanyScopeService companyScope, ICacheInvalidator cacheInvalidator, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
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
    [HttpGet("categories"), Authorize(Policy = "categories.view")]
    [OutputCache(PolicyName = "RefData", Tags = [CacheTags.Categories])] // Task P: reference-data, non-company-scoped (no CompanyId), same for all authorized users
    public async Task<IActionResult> GetCategories([FromQuery] CategoryType? type)
    {
        var query = _context.Categories.AsNoTracking();
        if (type.HasValue) query = query.Where(c => c.CategoryType == type.Value);
        var list = await query.OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, c.CategoryType, c.TagColor, c.CheckinEmail, c.RequireAcceptance, c.UseDefaultEula, c.Notes }).ToListAsync();
        return Ok(new { status = "success", data = list });
    }
    [HttpPost("categories"), Authorize(Policy = "categories.create")]
    public async Task<IActionResult> CreateCategory([FromBody] Category c)
    {
        var exists = await _context.Categories.AnyAsync(x => x.Name == c.Name && x.CategoryType == c.CategoryType);
        if (exists) return BadRequest(new { status = "error", message = "Tên danh mục đã tồn tại." });
        _context.Categories.Add(c);
        _actionLogService.Log(new ActionLogEntry { ItemType = ItemType.Category, ItemId = c.Id, ActionType = ActionType.Create, CreatedBy = GetCurrentUserId(), CompanyId = null, Note = $"Tạo danh mục \"{c.Name}\" (loại: {c.CategoryType})" });
        await _context.SaveChangesAsync();
        await _cacheInvalidator.InvalidateCategoriesAsync();
        return Ok(new { status = "success", data = new { c.Id, c.Name } });
    }
    [HttpPut("categories/{id:guid}"), Authorize(Policy = "categories.edit")]
    public async Task<IActionResult> UpdateCategory(Guid id, [FromBody] UpdateCategoryRequest updated)
    {
        var c = await _context.Categories.FindAsync(id);
        if (c == null) return NotFound(new { status = "error", message = "Category not found." });
        // Task M2 patch semantics: only fields explicitly sent are applied (absent → keep current).
        var before = new { c.Name, c.TagColor, c.CheckinEmail, c.RequireAcceptance, c.UseDefaultEula, c.Notes };
        var oldName = c.Name;
        if (!string.IsNullOrWhiteSpace(updated.Name)) c.Name = updated.Name;
        if (updated.TagColor is not null) c.TagColor = updated.TagColor;
        if (updated.CheckinEmail.HasValue) c.CheckinEmail = updated.CheckinEmail.Value;
        if (updated.RequireAcceptance.HasValue) c.RequireAcceptance = updated.RequireAcceptance.Value;
        if (updated.UseDefaultEula.HasValue) c.UseDefaultEula = updated.UseDefaultEula.Value;
        if (updated.Notes is not null) c.Notes = updated.Notes;
        // CategoryType cannot be changed after creation
        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.Category,
            ItemId = id,
            ActionType = ActionType.Update,
            CreatedBy = GetCurrentUserId(),
            CompanyId = null,
            LogMeta = JsonSerializer.Serialize(new { changes = new { name = new { old = before.Name, @new = c.Name }, tagColor = new { old = before.TagColor, @new = c.TagColor }, checkinEmail = new { old = before.CheckinEmail, @new = c.CheckinEmail }, requireAcceptance = new { old = before.RequireAcceptance, @new = c.RequireAcceptance }, useDefaultEula = new { old = before.UseDefaultEula, @new = c.UseDefaultEula }, notes = new { old = before.Notes, @new = c.Notes } } }),
            Note = $"Cập nhật danh mục \"{oldName}\""
        });
        await _context.SaveChangesAsync();
        await _cacheInvalidator.InvalidateCategoriesAsync();
        return Ok(new { status = "success", message = "Updated." });
    }
    [HttpDelete("categories/{id:guid}"), Authorize(Policy = "categories.delete")]
    public async Task<IActionResult> DeleteCategory(Guid id)
    {
        var c = await _context.Categories.FindAsync(id);
        if (c == null) return NotFound(new { status = "error", message = "Category not found." });

        // Guard: reject deletion while any entity still references this category
        // (components, asset models, consumables, accessories, licenses — incl. soft-deleted).
        var inUse =
            await _context.Components.IgnoreQueryFilters().AnyAsync(x => x.CategoryId == id) ||
            await _context.Models.AnyAsync(x => x.CategoryId == id) ||
            await _context.Consumables.AnyAsync(x => x.CategoryId == id) ||
            await _context.Accessories.AnyAsync(x => x.CategoryId == id) ||
            await _context.Licenses.AnyAsync(x => x.CategoryId == id);
        if (inUse)
            return BadRequest(new { status = "error", message = "Danh mục đang được sử dụng — không thể xóa.", error_code = "CATEGORY_IN_USE" });

        _actionLogService.Log(new ActionLogEntry { ItemType = ItemType.Category, ItemId = id, ActionType = ActionType.Delete, CreatedBy = GetCurrentUserId(), CompanyId = null, Note = $"Xóa danh mục \"{c.Name}\"" });
        _context.Categories.Remove(c);
        await _context.SaveChangesAsync();
        await _cacheInvalidator.InvalidateCategoriesAsync();
        return Ok(new { status = "success", message = "Deleted." });
    }

    // === Manufacturers ===
    [HttpGet("manufacturers"), Authorize(Policy = "manufacturers.view")]
    [OutputCache(PolicyName = "RefData", Tags = [CacheTags.Manufacturers])] // Task P: reference-data, no CompanyId, same for all authorized users
    public async Task<IActionResult> GetManufacturers()
    {
        var list = await _context.Manufacturers.AsNoTracking().OrderBy(m => m.Code).ToListAsync();
        return Ok(new { status = "success", data = list });
    }
    [HttpPost("manufacturers"), Authorize(Policy = "manufacturers.create")]
    public async Task<IActionResult> CreateManufacturer([FromBody] Manufacturer m)
    {
        if (string.IsNullOrWhiteSpace(m.Code) || m.Code.Length < 2 || m.Code.Length > 5)
            return BadRequest(new { status = "error", message = "Mã NSX phải từ 2-5 ký tự." });
        if (await _context.Manufacturers.AnyAsync(x => x.Code == m.Code))
            return BadRequest(new { status = "error", message = "Mã NSX đã tồn tại." });
        if (await _context.Manufacturers.AnyAsync(x => x.Name == m.Name))
            return BadRequest(new { status = "error", message = "Tên NSX đã tồn tại." });
        _context.Manufacturers.Add(m); await _context.SaveChangesAsync();
        _actionLogService.Log(new ActionLogEntry { ItemType = ItemType.Manufacturer, ItemId = m.Id, ActionType = ActionType.Create, CreatedBy = GetCurrentUserId(), CompanyId = null, Note = $"Tạo nhà sản xuất \"{m.Name}\"" });
        await _context.SaveChangesAsync();
        await _cacheInvalidator.InvalidateManufacturersAsync();
        return Ok(new { status = "success", data = new { m.Id, m.Code, m.Name } });
    }
    [HttpPut("manufacturers/{id:guid}"), Authorize(Policy = "manufacturers.edit")]
    public async Task<IActionResult> UpdateManufacturer(Guid id, [FromBody] Manufacturer updated)
    {
        var m = await _context.Manufacturers.FindAsync(id);
        if (m == null) return NotFound(new { status = "error", message = "Not found." });
        // Task M2 patch semantics: only fields explicitly sent are applied (absent → keep current).
        if (!string.IsNullOrWhiteSpace(updated.Code))
        {
            if (updated.Code.Length < 2 || updated.Code.Length > 5)
                return BadRequest(new { status = "error", message = "Mã NSX phải từ 2-5 ký tự." });
            if (await _context.Manufacturers.AnyAsync(x => x.Code == updated.Code && x.Id != id))
                return BadRequest(new { status = "error", message = "Mã NSX đã tồn tại." });
            m.Code = updated.Code;
        }
        if (!string.IsNullOrWhiteSpace(updated.Name))
        {
            if (await _context.Manufacturers.AnyAsync(x => x.Name == updated.Name && x.Id != id))
                return BadRequest(new { status = "error", message = "Tên NSX đã tồn tại." });
            m.Name = updated.Name;
        }
        var before = new { m.Code, m.Name, m.Url, m.SupportUrl, m.SupportEmail };
        if (updated.Url is not null) m.Url = updated.Url;
        if (updated.SupportUrl is not null) m.SupportUrl = updated.SupportUrl;
        if (updated.SupportEmail is not null) m.SupportEmail = updated.SupportEmail;
        await _context.SaveChangesAsync();
        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.Manufacturer,
            ItemId = id,
            ActionType = ActionType.Update,
            CreatedBy = GetCurrentUserId(),
            CompanyId = null,
            LogMeta = JsonSerializer.Serialize(new { changes = new { code = new { old = before.Code, @new = m.Code }, name = new { old = before.Name, @new = m.Name }, url = new { old = before.Url, @new = m.Url }, supportUrl = new { old = before.SupportUrl, @new = m.SupportUrl }, supportEmail = new { old = before.SupportEmail, @new = m.SupportEmail } } }),
            Note = $"Cập nhật nhà sản xuất \"{m.Name}\""
        });
        await _context.SaveChangesAsync();
        await _cacheInvalidator.InvalidateManufacturersAsync();
        return Ok(new { status = "success", message = "Updated." });
    }
    [HttpDelete("manufacturers/{id:guid}"), Authorize(Policy = "manufacturers.delete")]
    public async Task<IActionResult> DeleteManufacturer(Guid id)
    {
        var m = await _context.Manufacturers.FindAsync(id);
        if (m == null) return NotFound(new { status = "error", message = "Not found." });
        var mName = m.Name;
        // Delete guard: manufacturer referenced by inventory/models/licenses cannot be deleted.
        if (await _context.Components.IgnoreQueryFilters().AnyAsync(c => c.ManufacturerId == id)
            || await _context.Accessories.IgnoreQueryFilters().AnyAsync(a => a.ManufacturerId == id)
            || await _context.Consumables.IgnoreQueryFilters().AnyAsync(x => x.ManufacturerId == id)
            || await _context.Models.IgnoreQueryFilters().AnyAsync(m => m.ManufacturerId == id)
            || await _context.Licenses.IgnoreQueryFilters().AnyAsync(l => l.ManufacturerId == id && l.DeletedAt == null))
            return BadRequest(new { status = "error", message = "Nhà sản xuất đang được sản phẩm/model/bản quyền sử dụng — không thể xóa.", error_code = "MANUFACTURER_IN_USE" });
        _context.Manufacturers.Remove(m); await _context.SaveChangesAsync();
        _actionLogService.Log(new ActionLogEntry { ItemType = ItemType.Manufacturer, ItemId = id, ActionType = ActionType.Delete, CreatedBy = GetCurrentUserId(), CompanyId = null, Note = $"Xóa nhà sản xuất \"{mName}\"" });
        await _context.SaveChangesAsync();
        await _cacheInvalidator.InvalidateManufacturersAsync();
        return Ok(new { status = "success", message = "Deleted." });
    }

    // === Suppliers ===
    [HttpGet("suppliers"), Authorize(Policy = "suppliers.view")]
    [OutputCache(PolicyName = "RefData", Tags = [CacheTags.Suppliers])] // Task P: reference-data, no CompanyId, same for all authorized users
    public async Task<IActionResult> GetSuppliers()
    {
        var list = await _context.Suppliers.AsNoTracking().OrderBy(s => s.Code).ToListAsync();
        return Ok(new { status = "success", data = list });
    }
    [HttpPost("suppliers"), Authorize(Policy = "suppliers.create")]
    public async Task<IActionResult> CreateSupplier([FromBody] Supplier s)
    {
        if (string.IsNullOrWhiteSpace(s.Code) || s.Code.Length < 2 || s.Code.Length > 5)
            return BadRequest(new { status = "error", message = "Mã NCC phải từ 2-5 ký tự." });
        if (await _context.Suppliers.AnyAsync(x => x.Code == s.Code))
            return BadRequest(new { status = "error", message = "Mã NCC đã tồn tại." });
        if (await _context.Suppliers.AnyAsync(x => x.Name == s.Name))
            return BadRequest(new { status = "error", message = "Tên NCC đã tồn tại." });
        _context.Suppliers.Add(s); await _context.SaveChangesAsync();
        _actionLogService.Log(new ActionLogEntry { ItemType = ItemType.Supplier, ItemId = s.Id, ActionType = ActionType.Create, CreatedBy = GetCurrentUserId(), CompanyId = null, Note = $"Tạo nhà cung cấp \"{s.Name}\"" });
        await _context.SaveChangesAsync();
        await _cacheInvalidator.InvalidateSuppliersAsync();
        return Ok(new { status = "success", data = new { s.Id, s.Code, s.Name } });
    }
    [HttpPut("suppliers/{id:guid}"), Authorize(Policy = "suppliers.edit")]
    public async Task<IActionResult> UpdateSupplier(Guid id, [FromBody] Supplier updated)
    {
        var s = await _context.Suppliers.FindAsync(id);
        if (s == null) return NotFound(new { status = "error", message = "Not found." });
        // Task M2 patch semantics: only fields explicitly sent are applied (absent → keep current).
        if (!string.IsNullOrWhiteSpace(updated.Code))
        {
            if (updated.Code.Length < 2 || updated.Code.Length > 5)
                return BadRequest(new { status = "error", message = "Mã NCC phải từ 2-5 ký tự." });
            if (await _context.Suppliers.AnyAsync(x => x.Code == updated.Code && x.Id != id))
                return BadRequest(new { status = "error", message = "Mã NCC đã tồn tại." });
            s.Code = updated.Code;
        }
        if (!string.IsNullOrWhiteSpace(updated.Name))
        {
            if (await _context.Suppliers.AnyAsync(x => x.Name == updated.Name && x.Id != id))
                return BadRequest(new { status = "error", message = "Tên NCC đã tồn tại." });
            s.Name = updated.Name;
        }
        var before = new { s.Code, s.Name, s.Url, s.Address, s.City, s.State, s.Country, s.Zip, s.Phone, s.Fax, s.ContactName, s.ContactEmail };
        if (updated.Url is not null) s.Url = updated.Url;
        if (updated.Address is not null) s.Address = updated.Address;
        if (updated.City is not null) s.City = updated.City;
        if (updated.State is not null) s.State = updated.State;
        if (updated.Country is not null) s.Country = updated.Country;
        if (updated.Zip is not null) s.Zip = updated.Zip;
        if (updated.Phone is not null) s.Phone = updated.Phone;
        if (updated.Fax is not null) s.Fax = updated.Fax;
        if (updated.ContactName is not null) s.ContactName = updated.ContactName;
        if (updated.ContactEmail is not null) s.ContactEmail = updated.ContactEmail;
        await _context.SaveChangesAsync();
        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.Supplier,
            ItemId = id,
            ActionType = ActionType.Update,
            CreatedBy = GetCurrentUserId(),
            CompanyId = null,
            LogMeta = JsonSerializer.Serialize(new { changes = new { code = new { old = before.Code, @new = s.Code }, name = new { old = before.Name, @new = s.Name }, url = new { old = before.Url, @new = s.Url }, address = new { old = before.Address, @new = s.Address }, city = new { old = before.City, @new = s.City }, state = new { old = before.State, @new = s.State }, country = new { old = before.Country, @new = s.Country }, zip = new { old = before.Zip, @new = s.Zip }, phone = new { old = before.Phone, @new = s.Phone }, fax = new { old = before.Fax, @new = s.Fax }, contactName = new { old = before.ContactName, @new = s.ContactName }, contactEmail = new { old = before.ContactEmail, @new = s.ContactEmail } } }),
            Note = $"Cập nhật nhà cung cấp \"{s.Name}\""
        });
        await _context.SaveChangesAsync();
        await _cacheInvalidator.InvalidateSuppliersAsync();
        return Ok(new { status = "success", message = "Updated." });
    }
    [HttpDelete("suppliers/{id:guid}"), Authorize(Policy = "suppliers.delete")]
    public async Task<IActionResult> DeleteSupplier(Guid id)
    {
        var s = await _context.Suppliers.FindAsync(id);
        if (s == null) return NotFound(new { status = "error", message = "Not found." });
        var sName = s.Name;
        // Delete guard: supplier referenced by inventory (incl. asset) / licenses cannot be deleted.
        if (await _context.Components.IgnoreQueryFilters().AnyAsync(c => c.SupplierId == id)
            || await _context.Accessories.IgnoreQueryFilters().AnyAsync(a => a.SupplierId == id)
            || await _context.Consumables.IgnoreQueryFilters().AnyAsync(x => x.SupplierId == id)
            || await _context.Assets.IgnoreQueryFilters().AnyAsync(a => a.SupplierId == id)
            || await _context.Licenses.IgnoreQueryFilters().AnyAsync(l => l.SupplierId == id && l.DeletedAt == null))
            return BadRequest(new { status = "error", message = "Nhà cung cấp đang được sản phẩm/tài sản/bản quyền sử dụng — không thể xóa.", error_code = "SUPPLIER_IN_USE" });
        _context.Suppliers.Remove(s); await _context.SaveChangesAsync();
        _actionLogService.Log(new ActionLogEntry { ItemType = ItemType.Supplier, ItemId = id, ActionType = ActionType.Delete, CreatedBy = GetCurrentUserId(), CompanyId = null, Note = $"Xóa nhà cung cấp \"{sName}\"" });
        await _context.SaveChangesAsync();
        await _cacheInvalidator.InvalidateSuppliersAsync();
        return Ok(new { status = "success", message = "Deleted." });
    }

    // === Locations ===
    [HttpGet("locations"), Authorize(Policy = "locations.view")]
    public async Task<IActionResult> GetLocations([FromQuery] Guid? companyId)
    {
        // [Task U] Company-scoping: FORCE scope to the acting user's company (or floater) for a
        // regular user — never trust the optional `companyId` query param (omitting it used to
        // reveal every company's locations). Superuser may optionally filter by `companyId`.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var query = _context.Locations.AsNoTracking().AsQueryable();
        if (userCompanyId.HasValue)
            query = query.Where(l => l.CompanyId == null || l.CompanyId == userCompanyId.Value);
        else if (companyId.HasValue)
            query = query.Where(l => l.CompanyId == companyId.Value);
        var list = await query.OrderBy(l => l.Name)
            .Select(l => new { l.Id, l.Name, l.ParentId, l.CompanyId, l.ManagerId, l.Address, l.City, l.State, l.Country, l.Zip }).ToListAsync();
        return Ok(new { status = "success", data = list });
    }
    [HttpPost("locations"), Authorize(Policy = "locations.create")]
    public async Task<IActionResult> CreateLocation(Location l)
    {
        _context.Locations.Add(l); await _context.SaveChangesAsync();
        _actionLogService.Log(new ActionLogEntry { ItemType = ItemType.Location, ItemId = l.Id, ActionType = ActionType.Create, CreatedBy = GetCurrentUserId(), CompanyId = l.CompanyId, Note = $"Tạo địa điểm \"{l.Name}\"" });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", data = new { l.Id } });
    }
    [HttpPut("locations/{id:guid}"), Authorize(Policy = "locations.edit")]
    public async Task<IActionResult> UpdateLocation(Guid id, [FromBody] Location updated)
    {
        var l = await _context.Locations.FindAsync(id);
        if (l == null) return NotFound(new { status = "error", message = "Not found." });

        // Company scoping: a regular user may only edit locations of their own company (or floater).
        var userCompanyIdUpdate = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyIdUpdate.HasValue && l.CompanyId.HasValue && l.CompanyId.Value != userCompanyIdUpdate.Value)
            return NotFound(new { status = "error", message = "Not found." });

        // Task M2 patch semantics: only fields explicitly sent are applied (absent → keep current).
        var before = new { l.Name, l.ParentId, l.CompanyId, l.ManagerId, l.Address, l.City, l.State, l.Country, l.Zip };
        if (!string.IsNullOrWhiteSpace(updated.Name)) l.Name = updated.Name;
        if (updated.ParentId is not null) l.ParentId = updated.ParentId;
        if (updated.CompanyId is not null) l.CompanyId = updated.CompanyId;
        if (updated.ManagerId is not null) l.ManagerId = updated.ManagerId;
        if (updated.Address is not null) l.Address = updated.Address;
        if (updated.City is not null) l.City = updated.City;
        if (updated.State is not null) l.State = updated.State;
        if (updated.Country is not null) l.Country = updated.Country;
        if (updated.Zip is not null) l.Zip = updated.Zip;
        await _context.SaveChangesAsync();
        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.Location,
            ItemId = id,
            ActionType = ActionType.Update,
            CreatedBy = GetCurrentUserId(),
            CompanyId = l.CompanyId,
            LogMeta = JsonSerializer.Serialize(new { changes = new { name = new { old = before.Name, @new = l.Name }, parentId = new { old = before.ParentId, @new = l.ParentId }, companyId = new { old = before.CompanyId, @new = l.CompanyId }, managerId = new { old = before.ManagerId, @new = l.ManagerId }, address = new { old = before.Address, @new = l.Address }, city = new { old = before.City, @new = l.City }, state = new { old = before.State, @new = l.State }, country = new { old = before.Country, @new = l.Country }, zip = new { old = before.Zip, @new = l.Zip } } }),
            Note = $"Cập nhật địa điểm \"{l.Name}\""
        });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Updated." });
    }
    [HttpDelete("locations/{id:guid}"), Authorize(Policy = "locations.delete")]
    public async Task<IActionResult> DeleteLocation(Guid id)
    {
        var l = await _context.Locations.FindAsync(id);
        if (l == null) return NotFound(new { status = "error", message = "Not found." });

        // Company scoping: a regular user may only delete locations of their own company (or floater).
        var userCompanyIdDelete = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyIdDelete.HasValue && l.CompanyId.HasValue && l.CompanyId.Value != userCompanyIdDelete.Value)
            return NotFound(new { status = "error", message = "Not found." });

        var hasChildren = await _context.Locations.AnyAsync(x => x.ParentId == id);
        if (hasChildren) return BadRequest(new { status = "error", message = "Không thể xóa địa điểm có địa điểm con." });
        // Delete guard: a location referenced by inventory/users cannot be deleted.
        if (await _context.Components.IgnoreQueryFilters().AnyAsync(c => c.LocationId == id)
            || await _context.Assets.IgnoreQueryFilters().AnyAsync(a => a.LocationId == id)
            || await _context.Consumables.IgnoreQueryFilters().AnyAsync(x => x.LocationId == id)
            || await _context.Accessories.IgnoreQueryFilters().AnyAsync(a => a.LocationId == id)
            || await _context.Users.AnyAsync(u => u.LocationId == id))
            return BadRequest(new { status = "error", message = "Địa điểm đang được tài sản/vật tư/phụ kiện/người dùng sử dụng — không thể xóa.", error_code = "LOCATION_IN_USE" });
        var lName = l.Name;
        _context.Locations.Remove(l); await _context.SaveChangesAsync();
        _actionLogService.Log(new ActionLogEntry { ItemType = ItemType.Location, ItemId = id, ActionType = ActionType.Delete, CreatedBy = GetCurrentUserId(), CompanyId = l.CompanyId, Note = $"Xóa địa điểm \"{lName}\"" });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Deleted." });
    }

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

/// <summary>Patch-style Update DTO for Category (Task M2).</summary>
public record UpdateCategoryRequest(
    string? Name = null, string? TagColor = null, bool? CheckinEmail = null, bool? RequireAcceptance = null,
    bool? UseDefaultEula = null, string? Notes = null);