using System.Text.Json;
using System.Security.Claims;
using System.Text.RegularExpressions;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Web.Controllers;

[ApiController, Route("api/v1/system-infos"), Authorize]
public class SystemInfoController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public SystemInfoController(AppDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
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

    // Code format: XXX(X)-YYYY-ZZZ — 3-4 uppercase letters prefix (SYS/POS/SYST...), 4-digit year,
    // 3-digit per-year sequence. Prefix length 3..4 letters (accepted uppercase only after normalize).
    private static readonly Regex CodeRegex = new(@"^[A-Z]{3,4}-\d{4}-\d{3}$", RegexOptions.Compiled);

    [HttpGet, Authorize(Policy = "systems.view")]
    public async Task<IActionResult> GetAll()
    {
        // FMCS multi-tenant: only expose systems inside the current user's company scope.
        // Superuser (GetCurrentUserCompanyIdAsync returns null) sees everything; a regular user
        // only sees company-less or their own company's systems.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var query = _context.SystemInfos.AsNoTracking();
        if (userCompanyId.HasValue)
            query = query.Where(s => s.CompanyId == null || s.CompanyId == userCompanyId.Value);

        var list = await query
            .Include(s => s.Positions.OrderBy(p => p.Code))
            .Include(s => s.Company)
            .AsNoTracking()
            .OrderBy(s => s.Code)
            .Select(s => new
            {
                s.Id,
                s.Code,
                s.Name,
                s.Description,
                s.CompanyId,
                s.NextMaintenanceDueDate,
                Company = s.Company == null ? null : new { s.Company.Id, s.Company.Name },
                Positions = s.Positions.OrderBy(p => p.Code).Select(p => new
                {
                    p.Id,
                    p.Code,
                    p.Name,
                    p.Description,
                    SystemInfoId = p.SystemInfoId,
                    SystemInfoName = s.Name
                })
            })
            .ToListAsync();
        return Ok(new { status = "success", data = list });
    }

    [HttpGet("{id:guid}"), Authorize(Policy = "systems.view")]
    public async Task<IActionResult> Get(Guid id)
    {
        // Same company scope as GetAll: a regular user may only fetch systems inside their own
        // company scope (or company-less systems); 404 to avoid leaking existence of other systems.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var visible = await _context.SystemInfos.AsNoTracking().AnyAsync(x =>
            x.Id == id && (userCompanyId == null || x.CompanyId == null || x.CompanyId == userCompanyId.Value));
        if (!visible) return NotFound(new { status = "error", message = "Not found." });

        var s = await _context.SystemInfos
            .Include(x => x.Positions)
            .Include(x => x.Company)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return NotFound(new { status = "error", message = "Not found." });

        // Projection (NOT the raw entity): the entity graph is cyclic (SystemInfo → Positions →
        // SystemInfo) and would fail JSON serialization with a possible-object-cycle error.
        var data = new
        {
            s.Id,
            s.Code,
            s.Name,
            s.Description,
            s.CompanyId,
            s.NextMaintenanceDueDate,
            Company = s.Company == null ? null : new { s.Company.Id, s.Company.Name },
            Positions = s.Positions.OrderBy(p => p.Code).Select(p => new
            {
                p.Id,
                p.Code,
                p.Name,
                p.Description,
                SystemInfoId = p.SystemInfoId,
                SystemInfoName = s.Name
            })
        };
        return Ok(new { status = "success", data });
    }

    [HttpPost, Authorize(Policy = "systems.create")]
    public async Task<IActionResult> Create([FromBody] SystemInfoDto dto)
    {
        // Normalize code to uppercase FIRST so a lowercase client input is accepted and stored uppercase
        // (the user does not need to remember the case rule). Validation then runs on the normalized value.
        dto = dto with { Code = dto.Code?.Trim().ToUpperInvariant() ?? string.Empty };
        if (string.IsNullOrWhiteSpace(dto.Code) || !CodeRegex.IsMatch(dto.Code))
            return BadRequest(new { status = "error", message = "Mã hệ thống phải đúng định dạng XXX(X)-YYYY-ZZZ (3-4 chữ hoa, viết hoa)." });
        // [SEC-FIX P1] Code/Name are now nullable on the shared DTO — Create must still require both.
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { status = "error", message = "Tên hệ thống không được để trống." });
        if (await _context.SystemInfos.AnyAsync(x => x.Code == dto.Code))
            return BadRequest(new { status = "error", message = "Mã hệ thống đã tồn tại." });

        // [Task L2 — COMPANY-SCOPING on Create] A regular user may only create systems for their own
        // company (or a company-less floater). Superuser (GetCurrentUserCompanyIdAsync → null) may
        // create for any company. Never trust the client-supplied CompanyId alone.
        var userCompanyIdCreate = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyIdCreate.HasValue && dto.CompanyId.HasValue && dto.CompanyId.Value != userCompanyIdCreate.Value)
            return BadRequest(new { status = "error", message = "Bạn chỉ được tạo hệ thống cho công ty của mình.", error_code = "COMPANY_MISMATCH" });

        var sys = new SystemInfo { Code = dto.Code!.ToUpper(), Name = dto.Name!, Description = dto.Description, CompanyId = dto.CompanyId };
        _context.SystemInfos.Add(sys);
        await _context.SaveChangesAsync();
        _actionLogService.Log(new ActionLogEntry { ItemType = ItemType.SystemInfo, ItemId = sys.Id, ActionType = ActionType.Create, CreatedBy = GetCurrentUserId(), CompanyId = sys.CompanyId, Note = $"Tạo hệ thống \"{sys.Name}\"" });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", data = new { sys.Id, sys.Code, sys.Name } });
    }

    [HttpPut("{id:guid}"), Authorize(Policy = "systems.edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SystemInfoDto dto)
    {
        var s = await _context.SystemInfos.FindAsync(id);
        if (s == null) return NotFound(new { status = "error", message = "Not found." });

        // Company scoping: a regular user may only edit systems of their own company (or floater).
        var userCompanyIdUpdate = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyIdUpdate.HasValue && s.CompanyId.HasValue && s.CompanyId.Value != userCompanyIdUpdate.Value)
            return NotFound(new { status = "error", message = "Not found." });

        // Patch-aware validation: Code is only normalized/validated when it was ACTUALLY sent
        // (an absent field must not fail validation nor be overwritten — Task F/M1 pattern).
        string? normalizedCode = string.IsNullOrWhiteSpace(dto.Code) ? null : dto.Code.Trim().ToUpperInvariant();
        if (normalizedCode != null)
        {
            if (!CodeRegex.IsMatch(normalizedCode))
                return BadRequest(new { status = "error", message = "Mã hệ thống phải đúng định dạng XXX(X)-YYYY-ZZZ (3-4 chữ hoa, viết hoa)." });
            if (await _context.SystemInfos.AnyAsync(x => x.Code == normalizedCode && x.Id != id))
                return BadRequest(new { status = "error", message = "Mã hệ thống đã tồn tại." });
        }

        // [SEC-FIX P1] FIELD_LOCKED CompanyId — once the system is referenced by a child Position
        // (where Assets can be parked) or targeted by a LicenseSeat, moving it to another company
        // would silently re-scope those references. Mirrors Consumable-có-lịch-sử
        // (ConsumablesController FIELD_LOCKED) / License (:306-307) convention. Patch-aware: only
        // triggers when CompanyId is EXPLICITLY sent and DIFFERS — re-saving the same value passes.
        if (dto.CompanyId.HasValue && dto.CompanyId.Value != s.CompanyId
            && (await _context.SystemPositions.AnyAsync(p => p.SystemInfoId == id)
                || await _context.LicenseSeats.AnyAsync(ls => ls.SystemInfoId == id)))
            return BadRequest(new { status = "error", message = "Hệ thống đã có vị trí hoặc seat license tham chiếu — không thể đổi công ty.", error_code = "FIELD_LOCKED" });

        // ─── Patch semantics (Task F/M1 pattern): only fields explicitly sent are applied. ───
        var before = new { s.Code, s.Name, s.Description, s.CompanyId };
        if (normalizedCode != null) s.Code = normalizedCode.ToUpper();
        if (!string.IsNullOrWhiteSpace(dto.Name)) s.Name = dto.Name;
        if (dto.Description is not null) s.Description = dto.Description;
        if (dto.CompanyId.HasValue) s.CompanyId = dto.CompanyId;
        await _context.SaveChangesAsync();
        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.SystemInfo,
            ItemId = id,
            ActionType = ActionType.Update,
            CreatedBy = GetCurrentUserId(),
            CompanyId = s.CompanyId,
            LogMeta = JsonSerializer.Serialize(new { changes = new { code = new { old = before.Code, @new = s.Code }, name = new { old = before.Name, @new = s.Name }, description = new { old = before.Description, @new = s.Description }, companyId = new { old = before.CompanyId, @new = s.CompanyId } } }),
            Note = $"Cập nhật hệ thống \"{s.Name}\""
        });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Updated." });
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = "systems.delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var s = await _context.SystemInfos.FindAsync(id);
        if (s == null) return NotFound(new { status = "error", message = "Not found." });

        // Company scoping: a regular user may only delete systems of their own company (or floater).
        var userCompanyIdDelete = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyIdDelete.HasValue && s.CompanyId.HasValue && s.CompanyId.Value != userCompanyIdDelete.Value)
            return NotFound(new { status = "error", message = "Not found." });

        // [MC-7a delete-guard] Nếu có vị trí thuộc hệ thống này đang được ChecklistItem của template
        // bảo dưỡng tham chiếu → chặn (FK RESTRICT ở DB sẽ chặn cascade; guard trước để trả 400 mềm,
        // không để lộ 500 FK thô). Mirror delete-guard Company (AR-2).
        var posIds = await _context.SystemPositions.AsNoTracking()
            .Where(p => p.SystemInfoId == id)
            .Select(p => p.Id)
            .ToListAsync();
        if (posIds.Count > 0
            && await _context.MaintenanceChecklistItemPositions.AsNoTracking()
                .AnyAsync(ip => posIds.Contains(ip.SystemPositionId)))
            return BadRequest(new
            {
                status = "error",
                message = "Hệ thống có vị trí đang được ChecklistItem của template bảo dưỡng tham chiếu — không thể xóa.",
                error_code = "POSITION_IN_USE_BY_CHECKLIST"
            });

        // [BUG-C delete-guard] Campaign (kể cả Completed — lịch sử bất biến) tham chiếu SystemInfo qua
        // FK RESTRICT → xóa system sẽ lộ 500 FK thô (reproduced trong audit backend 2026-08-30).
        // Chặn trước bằng 400 mềm, cùng pattern AR-2/MC-7a: delete-guard by usage history.
        var campaignCount = await _context.MaintenanceCampaigns.AsNoTracking()
            .CountAsync(c => c.SystemInfoId == id);
        if (campaignCount > 0)
            return BadRequest(new
            {
                status = "error",
                message = $"Hệ thống đã có {campaignCount} đợt bảo dưỡng (lịch sử bất biến) — không thể xóa.",
                error_code = "SYSTEM_IN_USE_BY_CAMPAIGN"
            });

        var sysName = s.Name;
        _context.SystemInfos.Remove(s);
        await _context.SaveChangesAsync();
        _actionLogService.Log(new ActionLogEntry { ItemType = ItemType.SystemInfo, ItemId = id, ActionType = ActionType.Delete, CreatedBy = GetCurrentUserId(), CompanyId = s.CompanyId, Note = $"Xóa hệ thống \"{sysName}\"" });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Deleted." });
    }

    // === Positions ===

    [HttpPost("{systemInfoId:guid}/positions"), Authorize(Policy = "systems.create")]
    public async Task<IActionResult> AddPosition(Guid systemInfoId, [FromBody] SystemPositionDto dto)
    {
        dto = dto with { Code = dto.Code?.Trim().ToUpperInvariant() ?? string.Empty };
        if (string.IsNullOrWhiteSpace(dto.Code) || !CodeRegex.IsMatch(dto.Code))
            return BadRequest(new { status = "error", message = "Mã vị trí phải đúng định dạng XXX(X)-YYYY-ZZZ (3-4 chữ hoa, viết hoa)." });
        // [SEC-FIX P1] Code/Name nullable on the shared DTO — Create still requires both.
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { status = "error", message = "Tên vị trí không được để trống." });
        if (await _context.SystemPositions.AnyAsync(x => x.Code == dto.Code))
            return BadRequest(new { status = "error", message = "Mã vị trí đã tồn tại." });
        var sys = await _context.SystemInfos.FindAsync(systemInfoId);
        if (sys == null) return NotFound(new { status = "error", message = "System not found." });

        // [Task L2 — COMPANY-SCOPING on Create] A position inherits its parent system's CompanyId,
        // so a regular user may only add positions to systems in their own company scope (or a
        // company-less system). Superuser bypasses. Out-of-scope → 404 (hide existence, Task I).
        var userCompanyIdAddPos = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyIdAddPos.HasValue && sys.CompanyId.HasValue && sys.CompanyId.Value != userCompanyIdAddPos.Value)
            return NotFound(new { status = "error", message = "System not found." });

        var pos = new SystemPosition { SystemInfoId = systemInfoId, Code = dto.Code!.ToUpper(), Name = dto.Name!, Description = dto.Description };
        _context.SystemPositions.Add(pos);
        await _context.SaveChangesAsync();
        _actionLogService.Log(new ActionLogEntry { ItemType = ItemType.SystemPosition, ItemId = pos.Id, ActionType = ActionType.Create, CreatedBy = GetCurrentUserId(), CompanyId = sys.CompanyId, Note = $"Tạo vị trí \"{pos.Name}\" trong hệ thống \"{sys.Name}\"" });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", data = new { pos.Id, pos.Code, pos.Name } });
    }

    [HttpPut("{systemInfoId:guid}/positions/{posId:guid}"), Authorize(Policy = "systems.edit")]
    public async Task<IActionResult> UpdatePosition(Guid systemInfoId, Guid posId, [FromBody] SystemPositionDto dto)
    {
        var pos = await _context.SystemPositions.Include(p => p.SystemInfo)
            .FirstOrDefaultAsync(p => p.Id == posId && p.SystemInfoId == systemInfoId);
        if (pos == null) return NotFound(new { status = "error", message = "Position not found." });

        // Company scoping: a regular user may only edit positions of a system in their own company.
        var userCompanyIdUpdatePos = await _companyScope.GetCurrentUserCompanyIdAsync();
        var posCompanyId = pos.SystemInfo?.CompanyId;
        if (userCompanyIdUpdatePos.HasValue && posCompanyId.HasValue && posCompanyId.Value != userCompanyIdUpdatePos.Value)
            return NotFound(new { status = "error", message = "Position not found." });

        // Patch-aware validation: Code is only normalized/validated when actually sent
        // ([SEC-FIX P1] same class of fix as the parent SystemInfo Update above).
        string? normalizedCode = string.IsNullOrWhiteSpace(dto.Code) ? null : dto.Code.Trim().ToUpperInvariant();
        if (normalizedCode != null)
        {
            if (!CodeRegex.IsMatch(normalizedCode))
                return BadRequest(new { status = "error", message = "Mã vị trí phải đúng định dạng XXX(X)-YYYY-ZZZ (3-4 chữ hoa, viết hoa)." });
            if (await _context.SystemPositions.AnyAsync(x => x.Code == normalizedCode && x.Id != posId))
                return BadRequest(new { status = "error", message = "Mã vị trí đã tồn tại." });
        }

        // ─── Patch semantics (Task F/M1 pattern): only fields explicitly sent are applied. A
        // position has no own CompanyId — it inherits its parent SystemInfo's company. ───
        var before = new { pos.Code, pos.Name, pos.Description };
        if (normalizedCode != null) pos.Code = normalizedCode.ToUpper();
        if (!string.IsNullOrWhiteSpace(dto.Name)) pos.Name = dto.Name;
        if (dto.Description is not null) pos.Description = dto.Description;
        await _context.SaveChangesAsync();
        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.SystemPosition,
            ItemId = posId,
            ActionType = ActionType.Update,
            CreatedBy = GetCurrentUserId(),
            CompanyId = pos.SystemInfo?.CompanyId,
            LogMeta = JsonSerializer.Serialize(new { changes = new { code = new { old = before.Code, @new = pos.Code }, name = new { old = before.Name, @new = pos.Name }, description = new { old = before.Description, @new = pos.Description } } }),
            Note = $"Cập nhật vị trí \"{pos.Name}\""
        });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Position updated." });
    }

    [HttpDelete("{systemInfoId:guid}/positions/{posId:guid}"), Authorize(Policy = "systems.delete")]
    public async Task<IActionResult> DeletePosition(Guid systemInfoId, Guid posId)
    {
        var pos = await _context.SystemPositions.Include(p => p.SystemInfo)
            .FirstOrDefaultAsync(p => p.Id == posId && p.SystemInfoId == systemInfoId);
        if (pos == null) return NotFound(new { status = "error", message = "Position not found." });

        // Company scoping: a regular user may only delete positions of a system in their own company.
        var userCompanyIdDeletePos = await _companyScope.GetCurrentUserCompanyIdAsync();
        var posCompanyIdDelete = pos.SystemInfo?.CompanyId;
        if (userCompanyIdDeletePos.HasValue && posCompanyIdDelete.HasValue && posCompanyIdDelete.Value != userCompanyIdDeletePos.Value)
            return NotFound(new { status = "error", message = "Position not found." });

        // [MC-7a delete-guard] Vị trí đang được ChecklistItem của template bảo dưỡng tham chiếu →
        // chặn xóa (FK RESTRICT ở DB sẽ chặn; guard trước để trả 400 mềm, không lộ 500 FK thô).
        if (await _context.MaintenanceChecklistItemPositions.AsNoTracking()
                .AnyAsync(ip => ip.SystemPositionId == posId))
            return BadRequest(new
            {
                status = "error",
                message = "Vị trí đang được ChecklistItem của template bảo dưỡng tham chiếu — không thể xóa. Hãy điều chỉnh template (version mới) trước.",
                error_code = "POSITION_IN_USE_BY_CHECKLIST"
            });

        var posName = pos.Name;
        _context.SystemPositions.Remove(pos);
        await _context.SaveChangesAsync();
        _actionLogService.Log(new ActionLogEntry { ItemType = ItemType.SystemPosition, ItemId = posId, ActionType = ActionType.Delete, CreatedBy = GetCurrentUserId(), CompanyId = pos.SystemInfo?.CompanyId, Note = $"Xóa vị trí \"{posName}\"" });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Position deleted." });
    }
}

// [SEC-FIX P1] Patch-aware DTOs: Code/Name are nullable so a PARTIAL update payload does not
// wipe absent fields (Task F/M1 pattern). Create validates presence explicitly; Update assigns
// only what was actually sent.
public record SystemInfoDto(string? Code, string? Name, string? Description, Guid? CompanyId = null);
public record SystemPositionDto(string? Code, string? Name, string? Description);