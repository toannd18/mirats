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
        // [SEC-FIX CLAIM-CLEANUP, 2026-08-23] ONLY the local DB user id stamped by JIT
        // provisioning ("local_user_id") is used — Keycloak sub/preferred_username are never a
        // user identity source (bug-class 1; parsing `sub` returns the WRONG id). Absent claim →
        // Guid.Empty (fail closed), matching the CompanyScopeService pattern.
        if (Guid.TryParse(User.FindFirstValue("local_user_id"), out var local)) return local;
        return Guid.Empty;
    }

    // GET — returns flat list grouped into a tree, company-scoped per user (Task V).
    [HttpGet]
    [OutputCache(PolicyName = "RefDataCompanyScope", Tags = [CacheTags.Companies])] // Task V: cache key varies by company scope → per-scope isolation
    public async Task<IActionResult> GetAll()
    {
        // [Task V] Company-scoping (same class of fix as Departments.GetAll Task K / GetLocations Task U):
        // Superuser → full tree; regular user with a company → only that company's subtree; regular user
        // WITHOUT a company (JIT-created, Guid.Empty sentinel from GetCurrentUserCompanyIdAsync) → full tree
        // (decision 2026-08-23: a company-less regular user may still VIEW the company tree so they can be
        // assigned a company in the User UI — only access to company-scoped DATA is restricted).
        // Never trust a client-supplied param.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();

        List<Company> all;
        if (userCompanyId.HasValue && userCompanyId.Value != Guid.Empty && !_companyScope.IsSuperUser())
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
        // [SEC-FIX S5, 2026-08-23] Company-scoping, mirroring GetAll: Superuser → any company;
        // a regular user WITH a company → only companies inside their subtree (own company +
        // descendants — a child user may NOT read a parent/other-branch company by id);
        // a company-less regular user → still allowed to VIEW a company by id (consistent with
        // the decision that company-less users may view the tree in GetAll). Out-of-scope →
        // 404 (hide existence).
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && userCompanyId.Value != Guid.Empty && !_companyScope.IsSuperUser()
            && !await _companyScope.IsCompanyIdInUserScopeAsync(id))
            return NotFound(new { status = "error", message = "Not found" });

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
        // [SEC-FIX S5, 2026-08-23] Company-scoping on write: a regular user may only update
        // companies inside their subtree (own + descendants); Superuser → any company.
        // Out-of-scope → 404 (hide existence). Previously Update had NO scope check at all — a
        // user from a child/another-branch company could rename/re-parent any company.
        if (!await _companyScope.IsCompanyIdInUserScopeAsync(id))
            return NotFound(new { status = "error", message = "Not found" });

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
        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.Company,
            ItemId = id,
            ActionType = ActionType.Update,
            CreatedBy = GetCurrentUserId(),
            CompanyId = null,
            LogMeta = JsonSerializer.Serialize(new { changes = new { name = new { old = before.Name, @new = c.Name }, code = new { old = before.Code, @new = c.Code }, parentId = new { old = before.ParentId, @new = c.ParentId } } }),
            Note = $"Cập nhật công ty \"{c.Name}\""
        });
        await _context.SaveChangesAsync();
        await _cacheInvalidator.InvalidateCompaniesAsync();
        return Ok(new { status = "success", message = "Updated" });
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = "companies.delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        // [SEC-FIX S5, 2026-08-23] Company-scoping on write: a regular user may only delete
        // companies inside their subtree; Superuser → any company. Out-of-scope → 404 (hide
        // existence). Previously Delete had NO scope check at all.
        if (!await _companyScope.IsCompanyIdInUserScopeAsync(id))
            return NotFound(new { status = "error", message = "Not found" });

        var hasChildren = await _context.Companies.AnyAsync(x => x.ParentId == id);
        if (hasChildren)
            return BadRequest(new { status = "error", message = "Không thể xóa công ty có công ty con." });

        // [SEC-FIX AR-2, 2026-08-24] Full reference audit (AppDbContext + information_schema on the real
        // DB): FKs referencing companies = assets/consumables/accessories/licenses (SetNull),
        // components (Restrict), departments/system_infos/users (SetNull), companies.ParentId
        // (Restrict — covered by the hasChildren check above); locations.CompanyId and
        // asset_tag_counters.CompanyId are PLAIN COLUMNS without an FK. Previously only 6 inventory
        // tables were checked — deleting a company silently SetNull'd Departments/SystemInfos/Users
        // (the ExecuteUpdate below) and orphaned Locations, turning them into cross-company floaters.
        // Now EVERY referencing table blocks the delete, and a company still having assigned USERS is
        // blocked too (explicit decision 2026-08-24: no more silent floater-ing of assigned users).
        var blockers = new List<string>();
        if (await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.CompanyId == id)) blockers.Add("tài khoản người dùng");
        if (await _context.Locations.IgnoreQueryFilters().AnyAsync(l => l.CompanyId == id)) blockers.Add("địa điểm");
        if (await _context.Departments.IgnoreQueryFilters().AnyAsync(d => d.CompanyId == id)) blockers.Add("phòng ban");
        if (await _context.SystemInfos.IgnoreQueryFilters().AnyAsync(s => s.CompanyId == id)) blockers.Add("hệ thống");
        if (await _context.Components.IgnoreQueryFilters().AnyAsync(c => c.CompanyId == id)) blockers.Add("linh kiện");
        if (await _context.Assets.IgnoreQueryFilters().AnyAsync(a => a.CompanyId == id)) blockers.Add("tài sản");
        if (await _context.Consumables.IgnoreQueryFilters().AnyAsync(c => c.CompanyId == id)) blockers.Add("vật tư");
        if (await _context.Accessories.IgnoreQueryFilters().AnyAsync(a => a.CompanyId == id)) blockers.Add("phụ kiện");
        if (await _context.Licenses.IgnoreQueryFilters().AnyAsync(l => l.CompanyId == id && l.DeletedAt == null)) blockers.Add("bản quyền");
        if (await _context.AssetMaintenances.IgnoreQueryFilters().AnyAsync(m => m.CompanyId == id)) blockers.Add("bảo trì");
        if (blockers.Count > 0)
            return BadRequest(new { status = "error", message = $"Công ty đang được sử dụng bởi: {string.Join(", ", blockers)} — không thể xóa.", error_code = "COMPANY_IN_USE" });

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