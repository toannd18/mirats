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

[ApiController, Route("api/v1/companies"), Authorize(Policy = "companies.view")]
public class CompaniesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IActionLogService _actionLogService;
    public CompaniesController(AppDbContext context, ICompanyScopeService companyScope, ICacheInvalidator cacheInvalidator, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _cacheInvalidator = cacheInvalidator;
        _actionLogService = actionLogService;
    }

    private Guid GetCurrentUserId()
    {
        // JIT provisioning stamps the local DB user id as "local_user_id" (Keycloak sub ≠ local id).
        if (Guid.TryParse(User.FindFirstValue("local_user_id"), out var local)) return local;
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    // GET — returns flat list grouped into a tree, company-scoped per user (Task V).
    [HttpGet]
    [OutputCache(PolicyName = "RefDataCompanyScope", Tags = [CacheTags.Companies])] // Task V: cache key varies by company scope → per-scope isolation
    public async Task<IActionResult> GetAll()
    {
        // [Task V] Company-scoping (same class of fix as Departments.GetAll Task K / GetLocations Task U):
        // Superuser → full tree; regular user with a company → only that company's subtree; regular user
        // without a company → full tree (no restriction, matching the Departments/GetLocations convention
        // where a company-less regular user has no company filter). Never trust a client-supplied param.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();

        List<Company> all;
        if (userCompanyId.HasValue && !_companyScope.IsSuperUser())
        {
            all = await GetSubtreeAsync(userCompanyId.Value);
        }
        else
        {
            all = await _context.Companies.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
        }

        var roots = BuildTree(all, null);
        return Ok(new { status = "success", data = roots });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var c = await _context.Companies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound(new { status = "error", message = "Not found" });
        return Ok(new { status = "success", data = new { c.Id, c.Name, c.Code, c.ParentId, Children = new List<object>() } });
    }

    [HttpPost, Authorize(Policy = "companies.create")]
    public async Task<IActionResult> Create([FromBody] CompanyDto dto)
    {
        // [Task ASSET-TAG-AUTO] Code: admin may supply; otherwise auto-suggest from name.
        var code = string.IsNullOrWhiteSpace(dto.Code) ? await SuggestCodeAsync(dto.Name) : dto.Code.Trim().ToUpperInvariant();
        if (code == "NOCO") return BadRequest(new { status = "error", message = "\"NOCO\" là mã dành riêng cho tài sản không thuộc công ty, không được dùng." });
        if (await _context.Companies.AnyAsync(c => c.Code == code))
            return BadRequest(new { status = "error", message = $"Mã công ty '{code}' đã tồn tại." });

        var c = new Company { Name = dto.Name, Code = code, ParentId = dto.ParentId };
        _context.Companies.Add(c);
        await _context.SaveChangesAsync();
        _actionLogService.Log(new ActionLogEntry { ItemType = ItemType.Company, ItemId = c.Id, ActionType = ActionType.Create, CreatedBy = GetCurrentUserId(), CompanyId = null, Note = $"Tạo công ty \"{c.Name}\" (mã {code})" });
        await _context.SaveChangesAsync();
        await _cacheInvalidator.InvalidateCompaniesAsync();
        return Ok(new { status = "success", data = new { c.Id, c.Name, c.Code, c.ParentId } });
    }

    [HttpPut("{id:guid}"), Authorize(Policy = "companies.edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CompanyDto dto)
    {
        var c = await _context.Companies.FindAsync(id);
        if (c == null) return NotFound(new { status = "error", message = "Not found" });
        var before = new { c.Name, c.Code, c.ParentId };
        c.Name = dto.Name;
        c.ParentId = dto.ParentId;
        // Code is editable on update; validate NOCO + uniqueness when it changes.
        var code = string.IsNullOrWhiteSpace(dto.Code) ? c.Code : dto.Code.Trim().ToUpperInvariant();
        if (code == "NOCO") return BadRequest(new { status = "error", message = "\"NOCO\" là mã dành riêng cho tài sản không thuộc công ty, không được dùng." });
        if (code != c.Code && await _context.Companies.AnyAsync(x => x.Code == code && x.Id != id))
            return BadRequest(new { status = "error", message = $"Mã công ty '{code}' đã tồn tại." });
        c.Code = code;
        // Prevent circular reference: cannot set parent to itself or its children
        if (dto.ParentId.HasValue)
        {
            var descendantIds = await GetDescendantIdsAsync(id);
            if (dto.ParentId == id || descendantIds.Contains(dto.ParentId.Value))
                return BadRequest(new { status = "error", message = "Không thể chọn chính nó hoặc công ty con làm cha." });
        }
        await _context.SaveChangesAsync();
        _actionLogService.Log(new ActionLogEntry { ItemType = ItemType.Company, ItemId = id, ActionType = ActionType.Update, CreatedBy = GetCurrentUserId(), CompanyId = null,
            LogMeta = JsonSerializer.Serialize(new { changes = new { name = new { old = before.Name, @new = c.Name }, code = new { old = before.Code, @new = c.Code }, parentId = new { old = before.ParentId, @new = c.ParentId } } }), Note = $"Cập nhật công ty \"{c.Name}\"" });
        await _context.SaveChangesAsync();
        await _cacheInvalidator.InvalidateCompaniesAsync();
        return Ok(new { status = "success", message = "Updated" });
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = "companies.delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var hasChildren = await _context.Companies.AnyAsync(x => x.ParentId == id);
        if (hasChildren)
            return BadRequest(new { status = "error", message = "Không thể xóa công ty có công ty con." });

        // Delete guard: a company still referenced by inventory items cannot be deleted. Some FKs are
        // SET NULL (Asset/Consumable/Accessory), which would silently turn those items into company-less
        // "floaters" (visible cross-company); License is RESTRICT (a raw 500). Block explicitly instead.
        var inUse =
            await _context.Components.IgnoreQueryFilters().AnyAsync(c => c.CompanyId == id) ||
            await _context.Assets.IgnoreQueryFilters().AnyAsync(a => a.CompanyId == id) ||
            await _context.Consumables.IgnoreQueryFilters().AnyAsync(c => c.CompanyId == id) ||
            await _context.Accessories.IgnoreQueryFilters().AnyAsync(a => a.CompanyId == id) ||
            await _context.Licenses.IgnoreQueryFilters().AnyAsync(l => l.CompanyId == id && l.DeletedAt == null) ||
            await _context.AssetMaintenances.IgnoreQueryFilters().AnyAsync(m => m.CompanyId == id);
        if (inUse)
            return BadRequest(new { status = "error", message = "Công ty đang được tài sản/vật tư/phụ kiện/bản quyền/bảo trì sử dụng — không thể xóa.", error_code = "COMPANY_IN_USE" });

        // No inventory references the company anymore → safe to clear user references and delete.
        await _context.Users.Where(u => u.CompanyId == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.CompanyId, (Guid?)null));

        var c = await _context.Companies.FindAsync(id);
        if (c == null) return NotFound(new { status = "error", message = "Not found" });
        var cName = c.Name;
        _context.Companies.Remove(c);
        await _context.SaveChangesAsync();
        _actionLogService.Log(new ActionLogEntry { ItemType = ItemType.Company, ItemId = id, ActionType = ActionType.Delete, CreatedBy = GetCurrentUserId(), CompanyId = null, Note = $"Xóa công ty \"{cName}\"" });
        await _context.SaveChangesAsync();
        await _cacheInvalidator.InvalidateCompaniesAsync();
        return Ok(new { status = "success", message = "Deleted" });
    }

    private async Task<HashSet<Guid>> GetDescendantIdsAsync(Guid parentId)
    {
        var result = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(parentId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var children = await _context.Companies.Where(c => c.ParentId == current).Select(c => c.Id).ToListAsync();
            foreach (var childId in children)
            {
                if (result.Add(childId)) queue.Enqueue(childId);
            }
        }
        return result;
    }

    /// <summary>Returns the company subtree rooted at <paramref name="companyId"/> (the company + all descendants).</summary>
    private async Task<List<Company>> GetSubtreeAsync(Guid companyId)
    {
        var ids = new HashSet<Guid> { companyId };
        ids.UnionWith(await GetDescendantIdsAsync(companyId));
        return await _context.Companies.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    private static List<object> BuildTree(List<Company> all, Guid? parentId)
    {
        // A node is a root when its ParentId is null OR its parent is not in the visible set
        // (the subtree root of a scoped regular user — its parent is excluded by the scope filter).
        var visibleIds = new HashSet<Guid>(all.Select(c => c.Id));
        return all.Where(c => c.ParentId == parentId || (parentId == null && c.ParentId.HasValue && !visibleIds.Contains(c.ParentId.Value)))
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Code,
                c.ParentId,
                Children = BuildTree(all, c.Id)
            }).ToList<object>();
    }

    /// <summary>Auto-suggests a short unique company code from the name (Task ASSET-TAG-AUTO).
    /// Reuses the Manufacturer-style algorithm: uppercase letters/digits, strip diacritics, keep up to
    /// 4 chars; append a numeric suffix when the base is taken; never "NOCO" (reserved for floaters).</summary>
    private async Task<string> SuggestCodeAsync(string name)
    {
        var baseCode = StripDiacritics(name).Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant).ToArray();
        var sb = new System.Text.StringBuilder();
        foreach (var ch in baseCode) if (char.IsLetter(ch)) sb.Append(ch); // prefer letters for readability
        if (sb.Length == 0) sb.Append("CO");
        var letters = sb.ToString();
        var basePart = letters.Length > 4 ? letters[..4] : letters;

        var candidate = basePart;
        if (candidate == "NOCO") candidate = "CO" + candidate;
        var suffix = 2;
        while (await _context.Companies.AnyAsync(c => c.Code == candidate) || candidate == "NOCO")
        {
            var suffixStr = suffix.ToString();
            var prefixLen = Math.Max(0, 4 - suffixStr.Length);
            candidate = basePart[..Math.Min(basePart.Length, prefixLen)] + suffixStr;
            if (candidate == "NOCO") candidate = basePart + suffixStr;
            suffix++;
        }
        return candidate;
    }

    private static string StripDiacritics(string s)
    {
        var normalized = s.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var ch in normalized)
        {
            var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(ch);
        }
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }
}

public record CompanyDto(string Name, Guid? ParentId, string? Code = null);