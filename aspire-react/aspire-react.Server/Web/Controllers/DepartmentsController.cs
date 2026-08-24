using System.Text.Json;
using System.Security.Claims;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Web.Controllers;

[ApiController, Route("api/v1/departments"), Authorize]
public class DepartmentsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;
    public DepartmentsController(AppDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    private Guid GetCurrentUserId()
    {
        // [SEC-FIX CLAIM-CLEANUP, 2026-08-23] ONLY "local_user_id" (JIT-stamped). Keycloak
        // sub/preferred_username are never a user identity source (bug-class 1). Absent → Guid.Empty.
        if (Guid.TryParse(User.FindFirstValue("local_user_id"), out var local)) return local;
        return Guid.Empty;
    }

    [HttpGet, Authorize(Policy = "departments.view")]
    public async Task<IActionResult> GetAll([FromQuery] Guid? companyId)
    {
        // [Task K] Company-scoping: FORCE scope to the acting user's company (or floater) for a
        // regular user — never trust the optional `companyId` query param for scoping (a regular
        // user omitting the param used to see every company). Superuser may optionally filter.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var query = _context.Departments
            .Include(d => d.Company)
            .Include(d => d.Manager)
            .AsNoTracking()
            .AsQueryable();

        if (userCompanyId.HasValue)
            query = query.Where(d => d.CompanyId == null || d.CompanyId == userCompanyId.Value);
        else if (companyId.HasValue)
            query = query.Where(d => d.CompanyId == companyId.Value);

        var list = await query
            .OrderBy(d => d.Name)
            .Select(d => new
            {
                d.Id,
                d.Name,
                d.Phone,
                d.Fax,
                d.CompanyId,
                Company = d.Company == null ? null : new { d.Company.Id, d.Company.Name },
                Manager = d.Manager == null ? null : new { d.Manager.Id, d.Manager.Username, d.Manager.FirstName, d.Manager.LastName }
            })
            .ToListAsync();
        return Ok(new { status = "success", data = list });
    }

    [HttpGet("{id:guid}"), Authorize(Policy = "departments.view")]
    public async Task<IActionResult> Get(Guid id)
    {
        // [Task K] Company-scoping: a regular user may only view departments of their own company
        // (or floater). Out-of-scope → 404 to hide existence.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var d = await _context.Departments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (d == null || (userCompanyId.HasValue && d.CompanyId.HasValue && d.CompanyId.Value != userCompanyId.Value))
            return NotFound(new { status = "error", message = "Not found." });
        return Ok(new { status = "success", data = d });
    }

    [HttpPost, Authorize(Policy = "departments.create")]
    public async Task<IActionResult> Create([FromBody] Department d)
    {
        // [Task L2] Company-scoping on CREATE: a regular user may only create departments for their
        // own company (or company-less floater). Superuser (scope → null) may create for any company.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && d.CompanyId.HasValue && d.CompanyId.Value != userCompanyId.Value)
            return BadRequest(new { status = "error", message = "Bạn chỉ được tạo phòng ban cho công ty của mình.", error_code = "COMPANY_MISMATCH" });

        if (string.IsNullOrWhiteSpace(d.Name))
            return BadRequest(new { status = "error", message = "Tên phòng ban không được để trống." });
        if (await _context.Departments.AnyAsync(x => x.Name == d.Name))
            return BadRequest(new { status = "error", message = "Tên phòng ban đã tồn tại." });
        _context.Departments.Add(d);
        await _context.SaveChangesAsync();
        _actionLogService.Log(new ActionLogEntry { ItemType = ItemType.Department, ItemId = d.Id, ActionType = ActionType.Create, CreatedBy = GetCurrentUserId(), CompanyId = d.CompanyId, Note = $"Tạo phòng ban \"{d.Name}\"" });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", data = new { d.Id, d.Name } });
    }

    [HttpPut("{id:guid}"), Authorize(Policy = "departments.edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] Department updated)
    {
        var d = await _context.Departments.FindAsync(id);
        if (d == null) return NotFound(new { status = "error", message = "Not found." });

        // Company scoping: a regular user may only edit departments of their own company (or floater).
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && d.CompanyId.HasValue && d.CompanyId.Value != userCompanyId.Value)
            return NotFound(new { status = "error", message = "Not found." });

        if (string.IsNullOrWhiteSpace(updated.Name))
            return BadRequest(new { status = "error", message = "Tên phòng ban không được để trống." });
        if (await _context.Departments.AnyAsync(x => x.Name == updated.Name && x.Id != id))
            return BadRequest(new { status = "error", message = "Tên phòng ban đã tồn tại." });
        var before = new { d.Name, d.CompanyId, d.ManagerId, d.Phone, d.Fax };
        d.Name = updated.Name; d.CompanyId = updated.CompanyId;
        d.ManagerId = updated.ManagerId; d.Phone = updated.Phone; d.Fax = updated.Fax;
        await _context.SaveChangesAsync();
        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.Department,
            ItemId = id,
            ActionType = ActionType.Update,
            CreatedBy = GetCurrentUserId(),
            CompanyId = d.CompanyId,
            LogMeta = JsonSerializer.Serialize(new { changes = new { name = new { old = before.Name, @new = d.Name }, companyId = new { old = before.CompanyId, @new = d.CompanyId }, managerId = new { old = before.ManagerId, @new = d.ManagerId }, phone = new { old = before.Phone, @new = d.Phone }, fax = new { old = before.Fax, @new = d.Fax } } }),
            Note = $"Cập nhật phòng ban \"{d.Name}\""
        });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Updated." });
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = "departments.delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var d = await _context.Departments.FindAsync(id);
        if (d == null) return NotFound(new { status = "error", message = "Not found." });

        // Company scoping: a regular user may only delete departments of their own company (or floater).
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && d.CompanyId.HasValue && d.CompanyId.Value != userCompanyId.Value)
            return NotFound(new { status = "error", message = "Not found." });

        // Delete guard: a department still referenced by users or by an allocation/checkout target
        // must not be hard-deleted (would orphan the references / lose history).
        if (await _context.Users.AnyAsync(u => u.DepartmentId == id)
            || await _context.Assignments.IgnoreQueryFilters().AnyAsync(a => a.TargetType == AssignmentTargetType.Department && a.TargetId == id))
            return BadRequest(new { status = "error", message = "Phòng ban đang được người dùng / lịch sử cấp phát sử dụng — không thể xóa.", error_code = "DEPARTMENT_IN_USE" });
        _context.Departments.Remove(d);
        await _context.SaveChangesAsync();
        _actionLogService.Log(new ActionLogEntry { ItemType = ItemType.Department, ItemId = id, ActionType = ActionType.Delete, CreatedBy = GetCurrentUserId(), CompanyId = d.CompanyId, Note = $"Xóa phòng ban \"{d.Name}\"" });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Deleted." });
    }
}