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

/// <summary>
/// MC-2 — Maintenance checklist TEMPLATE CRUD + version lifecycle (draft → publish) + items/standard-params.
/// <para>
/// Lifecycle (thiết kế đã duyệt):
///  - Tạo Template → tự động có 1 version DRAFT số 1 (PublishedAt = null, IsCurrent = false).
///  - Items/StandardParams CRUD trong version DRAFT hoặc published CHƯA có campaign → tự do.
///  - POST publish → PublishedAt = UtcNow, IsCurrent = true, các version khác chuyển IsCurrent = false
///    (KHÔNG xóa). Publish lần nữa trên version đã publish → 400 VERSION_ALREADY_PUBLISHED.
///  - Version ĐÃ có bất kỳ MaintenanceCampaign tham chiếu → BẤT BIẾN: mọi edit/delete trả
///    400 TEMPLATE_VERSION_IN_USE (không bao giờ để lộ FK constraint thô của Postgres).
///    DB vẫn chặn lớp cuối bằng FK RESTRICT (migration AddMaintenanceChecklist).
/// </para>
/// Company-scoping (Q1): Template company-scoped; CompanyId = null là floater xem được bởi mọi công ty.
/// Regular user chỉ thấy floater + template công ty mình; Update/Delete ngoài phạm vi → 404 (hide
/// existence); Create gán company khác → 400 COMPANY_MISMATCH. Superuser thấy tất cả.
/// </summary>
[ApiController]
[Route("api/v1/maintenance/templates")]
[Authorize]
public class MaintenanceTemplatesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public MaintenanceTemplatesController(
        AppDbContext context,
        ICurrentUserService currentUserService,
        ICompanyScopeService companyScope,
        IActionLogService actionLogService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    private Guid GetCurrentUserId() => _currentUserService.GetLocalUserId();

    /// <summary>null = superuser (thấy tất cả). Guid.Empty = regular user chưa được gán công ty (chỉ thấy floater).</summary>
    private Task<Guid?> GetUserCompanyIdAsync() => _companyScope.GetCurrentUserCompanyIdAsync();

    /// <summary>timestamp with time zone columns MUST receive Kind=Utc (DateTime Kind convention).</summary>
    private static DateTime ToUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    // ==================== Shared lookups ====================

    /// <summary>Template lookup + visibility check. Out-of-scope → null → caller returns 404 (hide existence).</summary>
    private async Task<MaintenanceChecklistTemplate?> GetVisibleTemplateAsync(Guid id)
    {
        var userCompanyId = await GetUserCompanyIdAsync();
        var t = await _context.MaintenanceChecklistTemplates
            .Include(x => x.SystemInfo)
                .ThenInclude(s => s.Positions) // [MC-7d] vị trí hệ thống template cho multi-select
            .Include(x => x.Company)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return null;
        if (userCompanyId.HasValue && t.CompanyId.HasValue && t.CompanyId.Value != userCompanyId.Value)
            return null;
        return t;
    }

    private async Task<MaintenanceChecklistTemplateVersion?> GetVersionOfTemplateAsync(MaintenanceChecklistTemplate template, Guid versionId)
        => await _context.MaintenanceChecklistTemplateVersions
            .FirstOrDefaultAsync(v => v.Id == versionId && v.TemplateId == template.Id);

    /// <summary>The IMMUTABLE guard source of truth: any campaign pinning this version.</summary>
    private Task<bool> VersionHasCampaignsAsync(Guid versionId)
        => _context.MaintenanceCampaigns.AsNoTracking().AnyAsync(c => c.TemplateVersionId == versionId);

    /// <summary>Any campaign pinning ANY version of this template (drives FIELD_LOCKED / delete-guard).</summary>
    private async Task<bool> TemplateHasCampaignsAsync(Guid templateId)
    {
        var versionIds = await _context.MaintenanceChecklistTemplateVersions.AsNoTracking()
            .Where(v => v.TemplateId == templateId)
            .Select(v => v.Id)
            .ToListAsync();
        if (versionIds.Count == 0) return false;
        return await _context.MaintenanceCampaigns.AsNoTracking()
            .AnyAsync(c => versionIds.Contains(c.TemplateVersionId));
    }

    /// <summary>SystemInfo must exist AND be inside the caller's company scope (floater systems visible to all).</summary>
    private async Task<bool> IsSystemVisibleAsync(Guid systemInfoId)
    {
        var userCompanyId = await GetUserCompanyIdAsync();
        return await _context.SystemInfos.AsNoTracking().AnyAsync(s =>
            s.Id == systemInfoId &&
            (!userCompanyId.HasValue || s.CompanyId == null || s.CompanyId == userCompanyId.Value));
    }

    private void LogTemplateAction(
        ActionType actionType,
        MaintenanceChecklistTemplate template,
        string note,
        object? meta = null,
        Guid itemId = default)
    {
        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.MaintenanceChecklistTemplate,
            ItemId = itemId == default ? template.Id : itemId,
            ActionType = actionType,
            CreatedBy = GetCurrentUserId(),
            CompanyId = template.CompanyId,
            TargetSystemInfoId = template.SystemInfoId,
            TargetSystemInfoName = template.SystemInfo?.Name,
            LogMeta = meta == null ? null : JsonSerializer.Serialize(meta),
            Note = note
        });
    }

    // ==================== Template CRUD ====================

    [HttpGet]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> GetAll([FromQuery] Guid? systemInfoId)
    {
        var userCompanyId = await GetUserCompanyIdAsync();
        var query = _context.MaintenanceChecklistTemplates.AsNoTracking();
        if (userCompanyId.HasValue)
            query = query.Where(t => t.CompanyId == null || t.CompanyId == userCompanyId.Value);
        if (systemInfoId.HasValue && systemInfoId.Value != Guid.Empty)
            query = query.Where(t => t.SystemInfoId == systemInfoId.Value);

        var list = await query
            .OrderBy(t => t.Name)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.IsActive,
                t.CompanyId,
                Company = t.Company == null ? null : new { t.Company.Id, t.Company.Name },
                SystemInfo = new { t.SystemInfo.Id, t.SystemInfo.Code, t.SystemInfo.Name },
                VersionsCount = t.Versions.Count(),
                CampaignCount = t.Versions.SelectMany(v => v.Campaigns).Count(),
                CurrentVersion = t.Versions.Where(v => v.IsCurrent).Select(v => new
                {
                    v.Id,
                    v.VersionNumber,
                    v.PublishedAt,
                    ItemsCount = v.Items.Count(),
                    ParamsCount = v.Items.SelectMany(i => i.StandardParams).Count()
                }).FirstOrDefault()
            })
            .ToListAsync();
        return Ok(new { status = "success", data = list });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> Get(Guid id)
    {
        // Out-of-scope → 404 hide-existence (Q1).
        var t = await GetVisibleTemplateAsync(id);
        if (t == null) return NotFound(new { status = "error", message = "Not found." });

        var versions = await _context.MaintenanceChecklistTemplateVersions.AsNoTracking()
            .Where(v => v.TemplateId == id)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new
            {
                v.Id,
                v.VersionNumber,
                v.EffectiveFrom,
                v.PublishedAt,
                v.IsCurrent,
                ItemsCount = v.Items.Count(),
                ParamsCount = v.Items.SelectMany(i => i.StandardParams).Count(),
                CampaignCount = v.Campaigns.Count()
            })
            .ToListAsync();

        var data = new
        {
            t.Id,
            t.Name,
            t.IsActive,
            t.CompanyId,
            Company = t.Company == null ? null : new { t.Company.Id, t.Company.Name },
            SystemInfo = new
            {
                t.SystemInfo.Id,
                t.SystemInfo.Code,
                t.SystemInfo.Name,
                // [MC-7d] Vị trí của hệ thống template — nguồn options cho multi-select vị trí áp dụng
                // của hạng mục (cùng policy maintenance.templates, không phụ thuộc systems.view).
                Positions = t.SystemInfo.Positions.OrderBy(p => p.Code)
                    .Select(p => new { p.Id, p.Code, p.Name })
                    .ToList()
            },
            Versions = versions
        };
        return Ok(new { status = "success", data });
    }

    [HttpPost]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> Create([FromBody] CreateMaintenanceTemplateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { status = "error", message = "Tên template là bắt buộc.", error_code = "NAME_REQUIRED" });
        var name = dto.Name.Trim();
        if (dto.SystemInfoId is not { } systemInfoId || systemInfoId == Guid.Empty)
            return BadRequest(new { status = "error", message = "Hệ thống áp dụng (SystemInfoId) là bắt buộc.", error_code = "SYSTEM_INFO_REQUIRED" });

        // The template lives under a system the creator can see (404 hides out-of-scope systems).
        if (!await IsSystemVisibleAsync(systemInfoId))
            return NotFound(new { status = "error", message = "Hệ thống không tồn tại hoặc ngoài phạm vi công ty của bạn." });

        // [Task L2 — COMPANY-SCOPING on Create] Never trust client CompanyId: a regular user may only
        // create for their own company (or omit → floater). Superuser may target any existing company.
        var userCompanyId = await GetUserCompanyIdAsync();
        if (dto.CompanyId.HasValue)
        {
            if (!await _context.Companies.AsNoTracking().AnyAsync(c => c.Id == dto.CompanyId.Value))
                return BadRequest(new { status = "error", message = "Công ty không hợp lệ.", error_code = "INVALID_COMPANY" });
            if (userCompanyId.HasValue && dto.CompanyId.Value != userCompanyId.Value)
                return BadRequest(new { status = "error", message = "Bạn chỉ được tạo template cho công ty của mình.", error_code = "COMPANY_MISMATCH" });
        }

        // Unique (SystemInfoId, Name) — checked explicitly so the client gets a clean 400 instead of a raw Postgres unique violation (500).
        if (await _context.MaintenanceChecklistTemplates.AnyAsync(t => t.SystemInfoId == systemInfoId && t.Name == name))
            return BadRequest(new { status = "error", message = "Tên template đã tồn tại trong hệ thống này.", error_code = "TEMPLATE_NAME_TAKEN" });

        var userId = GetCurrentUserId();
        var template = new MaintenanceChecklistTemplate
        {
            Name = name,
            SystemInfoId = systemInfoId,
            CompanyId = dto.CompanyId,
            CreatedById = userId
        };
        // Draft version 1 — born unpublished; items/params are added to it before publishing.
        var draft = new MaintenanceChecklistTemplateVersion
        {
            TemplateId = template.Id,
            VersionNumber = 1,
            CreatedById = userId
        };
        template.Versions.Add(draft);
        _context.MaintenanceChecklistTemplates.Add(template);
        await _context.SaveChangesAsync();

        LogTemplateAction(ActionType.Create, template, $"Tạo template bảo dưỡng \"{template.Name}\"", new { draftVersionId = draft.Id, versionNumber = 1 });
        await _context.SaveChangesAsync();

        return Ok(new
        {
            status = "success",
            data = new
            {
                template.Id,
                template.Name,
                template.SystemInfoId,
                template.CompanyId,
                template.IsActive,
                InitialVersionId = draft.Id
            }
        });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateMaintenanceTemplateDto dto)
    {
        var t = await GetVisibleTemplateAsync(id);
        if (t == null) return NotFound(new { status = "error", message = "Not found." });

        // CompanyId is intentionally NOT updatable (re-scoping would silently move visibility of every
        // campaign pinned through its versions) — explicit change attempts get FIELD_LOCKED.
        if (dto.CompanyId.HasValue && dto.CompanyId.Value != t.CompanyId)
            return BadRequest(new { status = "error", message = "Không thể đổi công ty sau khi tạo template.", error_code = "FIELD_LOCKED" });

        // Patch-aware: a field counts as changed ONLY when it was explicitly sent AND differs
        // (Task F/M1 pattern — an absent field must never be treated as changed).
        var newName = string.IsNullOrWhiteSpace(dto.Name) ? null : dto.Name.Trim();
        var nameChanged = newName != null && newName != t.Name;
        var sysChanged = dto.SystemInfoId.HasValue && dto.SystemInfoId.Value != t.SystemInfoId;
        var activeChanged = dto.IsActive.HasValue && dto.IsActive.Value != t.IsActive;

        // SystemInfoId locked once any campaign pins one of this template's versions (moving systems
        // would orphan the historical context those campaigns were run against).
        if (sysChanged && await TemplateHasCampaignsAsync(id))
            return BadRequest(new
            {
                status = "error",
                message = "Template đã có đợt bảo dưỡng tham chiếu — không thể đổi hệ thống.",
                error_code = "FIELD_LOCKED"
            });

        if (nameChanged || sysChanged)
        {
            var effectiveSystemId = sysChanged ? dto.SystemInfoId!.Value : t.SystemInfoId;
            if (sysChanged && !await IsSystemVisibleAsync(effectiveSystemId))
                return NotFound(new { status = "error", message = "Hệ thống không tồn tại hoặc ngoài phạm vi công ty của bạn." });
            // Unique (SystemInfoId, Name) — explicit check → clean 400, not a raw Postgres unique violation.
            if (await _context.MaintenanceChecklistTemplates.AnyAsync(x =>
                    x.Id != id && x.SystemInfoId == effectiveSystemId &&
                    x.Name == (nameChanged ? newName : t.Name)))
                return BadRequest(new { status = "error", message = "Tên template đã tồn tại trong hệ thống này.", error_code = "TEMPLATE_NAME_TAKEN" });
        }

        if (!nameChanged && !sysChanged && !activeChanged)
            return Ok(new { status = "success", message = "Updated." }); // nothing actually sent/changed

        // Capture BEFORE values prior to mutation (LogMeta.changes must hold true olds).
        var beforeName = t.Name;
        var beforeSystemId = t.SystemInfoId;
        var beforeIsActive = t.IsActive;

        if (nameChanged) t.Name = newName!;
        if (sysChanged) t.SystemInfoId = dto.SystemInfoId!.Value;
        if (activeChanged) t.IsActive = dto.IsActive!.Value;
        await _context.SaveChangesAsync();

        LogTemplateAction(ActionType.Update, t, $"Cập nhật template bảo dưỡng \"{t.Name}\"", new
        {
            changes = new
            {
                name = new { old = beforeName, @new = t.Name },
                systemInfoId = new { old = beforeSystemId, @new = t.SystemInfoId },
                isActive = new { old = beforeIsActive, @new = t.IsActive }
            }
        });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Updated." });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var t = await GetVisibleTemplateAsync(id);
        if (t == null) return NotFound(new { status = "error", message = "Not found." });

        // Delete-guard by usage history: any campaign pinning ANY version of this template blocks the
        // hard delete (DB-level the campaign→version FK is RESTRICT — we surface it as a clean 400).
        if (await TemplateHasCampaignsAsync(id))
            return BadRequest(new
            {
                status = "error",
                message = "Template đang có đợt bảo dưỡng tham chiếu — không thể xóa.",
                error_code = "TEMPLATE_IN_USE"
            });

        var name = t.Name;
        var companyId = t.CompanyId;
        _context.MaintenanceChecklistTemplates.Remove(t); // versions/items/params cascade (config-only, no history left behind)
        await _context.SaveChangesAsync();

        LogTemplateAction(ActionType.Delete, t, $"Xóa template bảo dưỡng \"{name}\"");
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Deleted." });
    }

    // ==================== Versions ====================

    /// <summary>Tạo một version DRAFT mới (VersionNumber tự tăng). Chưa publish, chưa current.</summary>
    [HttpPost("{id:guid}/versions")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> CreateVersion(Guid id, [FromBody] CreateTemplateVersionDto? dto)
    {
        var t = await GetVisibleTemplateAsync(id);
        if (t == null) return NotFound(new { status = "error", message = "Not found." });

        var nextNumber = (await _context.MaintenanceChecklistTemplateVersions.AsNoTracking()
            .Where(v => v.TemplateId == id)
            .MaxAsync(v => (int?)v.VersionNumber) ?? 0) + 1;

        var version = new MaintenanceChecklistTemplateVersion
        {
            TemplateId = id,
            VersionNumber = nextNumber,
            EffectiveFrom = dto?.EffectiveFrom.HasValue == true ? ToUtc(dto.EffectiveFrom.Value) : null,
            CreatedById = GetCurrentUserId()
        };
        _context.MaintenanceChecklistTemplateVersions.Add(version);
        await _context.SaveChangesAsync();

        LogTemplateAction(ActionType.Create, t, $"Tạo bản nháp version {nextNumber} cho template \"{t.Name}\"",
            new { scope = "version", versionId = version.Id, versionNumber = nextNumber });
        await _context.SaveChangesAsync();

        return Ok(new
        {
            status = "success",
            data = new { version.Id, version.VersionNumber, version.EffectiveFrom, version.PublishedAt, version.IsCurrent }
        });
    }

    /// <summary>Chi tiết 1 version: đầy đủ items + standard params (đã sort).</summary>
    [HttpGet("{id:guid}/versions/{versionId:guid}")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> GetVersion(Guid id, Guid versionId)
    {
        var t = await GetVisibleTemplateAsync(id);
        if (t == null) return NotFound(new { status = "error", message = "Not found." });
        var version = await GetVersionOfTemplateAsync(t, versionId);
        if (version == null) return NotFound(new { status = "error", message = "Version not found." });

        var hasCampaigns = await VersionHasCampaignsAsync(versionId);
        var data = new
        {
            version.Id,
            version.VersionNumber,
            version.EffectiveFrom,
            version.PublishedAt,
            version.IsCurrent,
            HasCampaigns = hasCampaigns,
            Editable = !hasCampaigns,
            Items = await _context.MaintenanceChecklistItems.AsNoTracking()
                .Include(i => i.Positions).ThenInclude(p => p.SystemPosition)
                .Include(i => i.StandardParams)
                .Where(i => i.TemplateVersionId == versionId)
                .OrderBy(i => i.Order)
                .Select(i => new
                {
                    i.Id,
                    i.Order,
                    i.Name,
                    i.CycleMonths,
                    i.ToolsRequired,
                    i.Instruction,
                    // [MC-7b] Phạm vi vị trí: [] = universal (mọi vị trí); kèm names để UI hiển thị.
                    PositionIds = i.Positions.Select(p => p.SystemPositionId).ToList(),
                    PositionNames = i.Positions.Select(p => p.SystemPosition != null ? p.SystemPosition.Name : null).ToList(),
                    // [MC-8] Tiêu chuẩn kỹ thuật NESTED trong từng hạng mục (thuộc tính con), không còn mảng song song.
                    // [MC-10] Ngưỡng cấu trúc: ThresholdOperator (string) + ThresholdValue (number).
                    StandardParams = i.StandardParams
                        .OrderBy(p => p.ParamName)
                        .Select(p => new { p.Id, p.ParamName, p.NominalValue, p.ThresholdOperator, p.ThresholdValue, p.CheckMethod, p.Unit })
                        .ToList()
                })
                .ToListAsync()
        };
        return Ok(new { status = "success", data });
    }

    /// <summary>
    /// Publish: draft → hiện hành. Set PublishedAt=UtcNow + IsCurrent=true, các version khác chuyển
    /// IsCurrent=false (không xóa). Idempotency guard: publish lại version đã publish → VERSION_ALREADY_PUBLISHED.
    /// </summary>
    [HttpPost("{id:guid}/versions/{versionId:guid}/publish")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> PublishVersion(Guid id, Guid versionId)
    {
        var t = await GetVisibleTemplateAsync(id);
        if (t == null) return NotFound(new { status = "error", message = "Not found." });
        var version = await GetVersionOfTemplateAsync(t, versionId);
        if (version == null) return NotFound(new { status = "error", message = "Version not found." });

        if (version.PublishedAt.HasValue)
            return BadRequest(new
            {
                status = "error",
                message = $"Version {version.VersionNumber} đã được publish trước đó.",
                error_code = "VERSION_ALREADY_PUBLISHED"
            });

        // ── Demote FIRST, promote SECOND — inside ONE explicit transaction. ──
        // Postgres enforces the filtered unique index ("IsCurrent" = true) PER STATEMENT: promoting
        // the new current before demoting the old one momentarily leaves TWO current rows and fails
        // with a raw 23505 → HTTP 500 (reproduced live; InMemory tests cannot catch this because
        // they don't enforce unique indexes). Ordered saves keep every intermediate state valid
        // while the transaction keeps the flip atomic.
        //
        // ⚠️ Aspire's AddNpgsqlDbContext registers a RETRYING execution strategy — a user-initiated
        // transaction is ONLY legal inside CreateExecutionStrategy().ExecuteAsync (same convention
        // as ComponentsController/Checkout handlers). A bare BeginTransactionAsync throws
        // InvalidOperationException → HTTP 500 before touching a single row.
        var strategy = _context.Database.CreateExecutionStrategy();
        Guid[] demotedIds = Array.Empty<Guid>();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync();

            var others = await _context.MaintenanceChecklistTemplateVersions
                .Where(v => v.TemplateId == id && v.IsCurrent && v.Id != version.Id)
                .ToListAsync();
            foreach (var other in others) other.IsCurrent = false;
            demotedIds = others.Select(o => o.Id).ToArray();
            await _context.SaveChangesAsync();

            version.PublishedAt = DateTime.UtcNow;
            if (!version.EffectiveFrom.HasValue) version.EffectiveFrom = version.PublishedAt;
            version.IsCurrent = true;
            await _context.SaveChangesAsync();

            await tx.CommitAsync();
        });

        LogTemplateAction(ActionType.Publish, t, $"Publish version {version.VersionNumber} cho template \"{t.Name}\"",
            new { scope = "version", versionId = version.Id, versionNumber = version.VersionNumber, demotedVersionIds = demotedIds },
            itemId: version.Id);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            status = "success",
            data = new { version.Id, version.VersionNumber, version.EffectiveFrom, version.PublishedAt, version.IsCurrent }
        });
    }

    /// <summary>Sửa metadata version (hiện chỉ EffectiveFrom). Version đã có campaign → TEMPLATE_VERSION_IN_USE.</summary>
    [HttpPut("{id:guid}/versions/{versionId:guid}")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> UpdateVersion(Guid id, Guid versionId, [FromBody] UpdateTemplateVersionDto dto)
    {
        var t = await GetVisibleTemplateAsync(id);
        if (t == null) return NotFound(new { status = "error", message = "Not found." });
        var version = await GetVersionOfTemplateAsync(t, versionId);
        if (version == null) return NotFound(new { status = "error", message = "Version not found." });

        // ── IMMUTABLE GUARD (MC-2 core): campaigns pinning this version freeze it entirely. ──
        if (await VersionHasCampaignsAsync(versionId))
            return BadRequest(new
            {
                status = "error",
                message = $"Version {version.VersionNumber} đã có đợt bảo dưỡng tham chiếu — không thể sửa. Hãy tạo version mới.",
                error_code = "TEMPLATE_VERSION_IN_USE"
            });

        var before = version.EffectiveFrom;
        if (dto.EffectiveFrom.HasValue)
        {
            version.EffectiveFrom = ToUtc(dto.EffectiveFrom.Value);
            await _context.SaveChangesAsync();
            LogTemplateAction(ActionType.Update, t, $"Cập nhật version {version.VersionNumber} của template \"{t.Name}\"",
                new { scope = "version", versionId = version.Id, changes = new { effectiveFrom = new { old = before, @new = version.EffectiveFrom } } },
                itemId: version.Id);
            await _context.SaveChangesAsync();
        }
        return Ok(new
        {
            status = "success",
            data = new { version.Id, version.VersionNumber, version.EffectiveFrom, version.PublishedAt, version.IsCurrent }
        });
    }

    /// <summary>Xóa version CHƯA publish. Đã publish → VERSION_ALREADY_PUBLISHED; có campaign → TEMPLATE_VERSION_IN_USE.</summary>
    [HttpDelete("{id:guid}/versions/{versionId:guid}")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> DeleteVersion(Guid id, Guid versionId)
    {
        var t = await GetVisibleTemplateAsync(id);
        if (t == null) return NotFound(new { status = "error", message = "Not found." });
        var version = await GetVersionOfTemplateAsync(t, versionId);
        if (version == null) return NotFound(new { status = "error", message = "Version not found." });

        // Guard order matters: IN_USE (campaign) beats ALREADY_PUBLISHED — the more specific problem first.
        if (await VersionHasCampaignsAsync(versionId))
            return BadRequest(new
            {
                status = "error",
                message = $"Version {version.VersionNumber} đã có đợt bảo dưỡng tham chiếu — không thể xóa.",
                error_code = "TEMPLATE_VERSION_IN_USE"
            });
        if (version.PublishedAt.HasValue)
            return BadRequest(new
            {
                status = "error",
                message = $"Version {version.VersionNumber} đã publish — không thể xóa (chỉ version nháp được xóa).",
                error_code = "VERSION_ALREADY_PUBLISHED"
            });

        var number = version.VersionNumber;
        _context.MaintenanceChecklistTemplateVersions.Remove(version); // items/params cascade
        await _context.SaveChangesAsync();

        LogTemplateAction(ActionType.Delete, t, $"Xóa bản nháp version {number} của template \"{t.Name}\"",
            new { scope = "version", versionId, versionNumber = number }, itemId: version.Id);
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Version deleted." });
    }

    // ==================== Checklist Items ====================

    /// <summary>Guard chung cho mọi thao tác ghi vào nội dung version: có campaign → TEMPLATE_VERSION_IN_USE.</summary>
    private async Task<MaintenanceChecklistTemplateVersion?> GetEditableVersionAsync(MaintenanceChecklistTemplate template, Guid versionId)
    {
        var version = await GetVersionOfTemplateAsync(template, versionId);
        if (version == null) return null;
        if (await VersionHasCampaignsAsync(versionId)) return null;
        return version;
    }

    /// <summary>
    /// [MC-7b] Mọi vị trí được khai báo phải TỒN TẠI và THUỘC ĐÚNG SystemInfo của template
    /// (per-system từ MC-1) — nếu không, khớp Position sẽ không bao giờ đúng → trả 400 INVALID_POSITION.
    /// null/[] = universal (không validate, không tạo row nào) — xử lý ở caller.
    /// </summary>
    private async Task<IActionResult?> ValidatePositionsAsync(MaintenanceChecklistTemplate template, Guid[]? positionIds)
    {
        if (positionIds == null) return null;
        var distinct = positionIds.Distinct().ToArray();
        if (distinct.Length == 0) return null;
        var found = await _context.SystemPositions.AsNoTracking()
            .Where(p => distinct.Contains(p.Id) && p.SystemInfoId == template.SystemInfoId)
            .Select(p => p.Id)
            .ToListAsync();
        if (found.Count != distinct.Length)
            return BadRequest(new
            {
                status = "error",
                message = "Có vị trí không tồn tại hoặc không thuộc hệ thống của template.",
                error_code = "INVALID_POSITION"
            });
        return null;
    }

    /// <summary>[MC-7b] Thay toàn bộ danh sách vị trí của item. PositionIds null = không đụng (patch); [] = universal.</summary>
    private async Task ReplaceItemPositionsAsync(MaintenanceChecklistItem item, Guid[]? positionIds)
    {
        if (positionIds == null) return;
        var existing = await _context.MaintenanceChecklistItemPositions
            .Where(ip => ip.ItemId == item.Id)
            .ToListAsync();
        _context.MaintenanceChecklistItemPositions.RemoveRange(existing);
        foreach (var pid in positionIds.Distinct())
        {
            _context.MaintenanceChecklistItemPositions.Add(new MaintenanceChecklistItemPosition
            {
                ItemId = item.Id,
                SystemPositionId = pid
            });
        }
    }

    [HttpPost("{id:guid}/versions/{versionId:guid}/items")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> AddItem(Guid id, Guid versionId, [FromBody] MaintenanceChecklistItemDto dto)
    {
        var t = await GetVisibleTemplateAsync(id);
        if (t == null) return NotFound(new { status = "error", message = "Not found." });
        var version = await GetEditableVersionAsync(t, versionId);
        if (version == null)
        {
            // Distinguish "not found" from "frozen": re-check existence for the right error shape.
            var exists = await GetVersionOfTemplateAsync(t, versionId);
            if (exists == null) return NotFound(new { status = "error", message = "Version not found." });
            return FrozenVersion(exists);
        }

        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { status = "error", message = "Tên hạng mục kiểm tra là bắt buộc.", error_code = "ITEM_NAME_REQUIRED" });
        if (dto.CycleMonths.HasValue && dto.CycleMonths.Value <= 0)
            return BadRequest(new { status = "error", message = "Chu kỳ (tháng) phải lớn hơn 0.", error_code = "INVALID_CYCLE_MONTHS" });
        if (await ValidatePositionsAsync(t, dto.PositionIds) is { } posErr) return posErr;

        var order = dto.Order ?? (await _context.MaintenanceChecklistItems.AsNoTracking()
            .Where(i => i.TemplateVersionId == versionId)
            .MaxAsync(i => (int?)i.Order) ?? 0) + 1;
        if (order <= 0)
            return BadRequest(new { status = "error", message = "Thứ tự (Order) phải lớn hơn 0.", error_code = "INVALID_ORDER" });
        if (await _context.MaintenanceChecklistItems.AnyAsync(i => i.TemplateVersionId == versionId && i.Order == order))
            return BadRequest(new { status = "error", message = $"Thứ tự {order} đã có hạng mục khác sử dụng.", error_code = "ITEM_ORDER_TAKEN" });

        var item = new MaintenanceChecklistItem
        {
            TemplateVersionId = versionId,
            Order = order,
            Name = dto.Name.Trim(),
            CycleMonths = dto.CycleMonths ?? 12,
            ToolsRequired = dto.ToolsRequired,
            Instruction = dto.Instruction
        };
        // [MC-7b] Khai báo phạm vi vị trí ngay lúc tạo (null/[] = universal → không tạo dòng nào).
        foreach (var pid in dto.PositionIds?.Distinct() ?? Array.Empty<Guid>())
        {
            item.Positions.Add(new MaintenanceChecklistItemPosition { SystemPositionId = pid });
        }
        _context.MaintenanceChecklistItems.Add(item);
        await _context.SaveChangesAsync();

        LogTemplateAction(ActionType.Create, t, $"Thêm hạng mục \"{item.Name}\" vào version {version.VersionNumber}",
            new { scope = "item", versionId, versionNumber = version.VersionNumber, itemId = item.Id, item.Order, positionIds = dto.PositionIds ?? Array.Empty<Guid>() });
        await _context.SaveChangesAsync();

        return Ok(new
        {
            status = "success",
            data = new
            {
                item.Id, item.Order, item.Name, item.CycleMonths, item.ToolsRequired, item.Instruction,
                PositionIds = dto.PositionIds ?? Array.Empty<Guid>()
            }
        });
    }

    [HttpPut("{id:guid}/versions/{versionId:guid}/items/{itemId:guid}")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> UpdateItem(Guid id, Guid versionId, Guid itemId, [FromBody] MaintenanceChecklistItemDto dto)
    {
        var t = await GetVisibleTemplateAsync(id);
        if (t == null) return NotFound(new { status = "error", message = "Not found." });
        var version = await GetVersionOfTemplateAsync(t, versionId);
        if (version == null) return NotFound(new { status = "error", message = "Version not found." });
        var item = await _context.MaintenanceChecklistItems.FirstOrDefaultAsync(i => i.Id == itemId && i.TemplateVersionId == versionId);
        if (item == null) return NotFound(new { status = "error", message = "Item not found." });
        if (await VersionHasCampaignsAsync(versionId)) return FrozenVersion(version);

        if (dto.CycleMonths.HasValue && dto.CycleMonths.Value <= 0)
            return BadRequest(new { status = "error", message = "Chu kỳ (tháng) phải lớn hơn 0.", error_code = "INVALID_CYCLE_MONTHS" });
        // [MC-7b] Nếu PositionIds ĐƯỢC GỬI (null/[] = universal, danh sách = khai báo) → validate + thay toàn bộ.
        if (dto.PositionIds != null)
        {
            if (await ValidatePositionsAsync(t, dto.PositionIds) is { } posErr) return posErr;
            await ReplaceItemPositionsAsync(item, dto.PositionIds);
        }

        int? newOrder = null;
        if (dto.Order.HasValue && dto.Order.Value != item.Order)
        {
            if (dto.Order.Value <= 0)
                return BadRequest(new { status = "error", message = "Thứ tự (Order) phải lớn hơn 0.", error_code = "INVALID_ORDER" });
            if (await _context.MaintenanceChecklistItems.AnyAsync(i => i.TemplateVersionId == versionId && i.Order == dto.Order.Value && i.Id != itemId))
                return BadRequest(new { status = "error", message = $"Thứ tự {dto.Order.Value} đã có hạng mục khác sử dụng.", error_code = "ITEM_ORDER_TAKEN" });
            newOrder = dto.Order.Value;
        }

        // Patch semantics: absent fields NEVER overwrite real data.
        var before = new { item.Order, item.Name, item.CycleMonths, item.ToolsRequired, item.Instruction };
        if (newOrder.HasValue) item.Order = newOrder.Value;
        if (!string.IsNullOrWhiteSpace(dto.Name)) item.Name = dto.Name.Trim();
        if (dto.CycleMonths.HasValue) item.CycleMonths = dto.CycleMonths.Value;
        if (dto.ToolsRequired is not null) item.ToolsRequired = dto.ToolsRequired;
        if (dto.Instruction is not null) item.Instruction = dto.Instruction;
        await _context.SaveChangesAsync();

        LogTemplateAction(ActionType.Update, t, $"Sửa hạng mục \"{item.Name}\" (version {version.VersionNumber})", new
        {
            scope = "item",
            versionId,
            versionNumber = version.VersionNumber,
            itemId,
            changes = new
            {
                order = new { old = before.Order, @new = item.Order },
                name = new { old = before.Name, @new = item.Name },
                cycleMonths = new { old = before.CycleMonths, @new = item.CycleMonths },
                toolsRequired = new { old = before.ToolsRequired, @new = item.ToolsRequired },
                instruction = new { old = before.Instruction, @new = item.Instruction }
            }
        });
        await _context.SaveChangesAsync();

        return Ok(new { status = "success", message = "Item updated." });
    }

    [HttpDelete("{id:guid}/versions/{versionId:guid}/items/{itemId:guid}")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> DeleteItem(Guid id, Guid versionId, Guid itemId)
    {
        var t = await GetVisibleTemplateAsync(id);
        if (t == null) return NotFound(new { status = "error", message = "Not found." });
        var version = await GetVersionOfTemplateAsync(t, versionId);
        if (version == null) return NotFound(new { status = "error", message = "Version not found." });
        var item = await _context.MaintenanceChecklistItems.FirstOrDefaultAsync(i => i.Id == itemId && i.TemplateVersionId == versionId);
        if (item == null) return NotFound(new { status = "error", message = "Item not found." });
        if (await VersionHasCampaignsAsync(versionId)) return FrozenVersion(version);

        var name = item.Name;
        _context.MaintenanceChecklistItems.Remove(item);
        await _context.SaveChangesAsync();

        LogTemplateAction(ActionType.Delete, t, $"Xóa hạng mục \"{name}\" khỏi version {version.VersionNumber}",
            new { scope = "item", versionId, versionNumber = version.VersionNumber, itemId });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Item deleted." });
    }

    // ==================== Standard Params ====================

    [HttpPost("{id:guid}/versions/{versionId:guid}/items/{itemId:guid}/standard-params")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> AddParam(Guid id, Guid versionId, Guid itemId, [FromBody] MaintenanceStandardParamDto dto)
    {
        var t = await GetVisibleTemplateAsync(id);
        if (t == null) return NotFound(new { status = "error", message = "Not found." });
        var version = await GetEditableVersionAsync(t, versionId);
        if (version == null)
        {
            var exists = await GetVersionOfTemplateAsync(t, versionId);
            if (exists == null) return NotFound(new { status = "error", message = "Version not found." });
            return FrozenVersion(exists);
        }
        var item = await _context.MaintenanceChecklistItems.FirstOrDefaultAsync(i => i.Id == itemId && i.TemplateVersionId == versionId);
        if (item == null) return NotFound(new { status = "error", message = "Item not found." });

        if (string.IsNullOrWhiteSpace(dto.ParamName))
            return BadRequest(new { status = "error", message = "Tên thông số là bắt buộc.", error_code = "PARAM_REQUIRED" });

        // [MC-10] Ngưỡng BẮT BUỘC cấu trúc (Operator + Value) — máy tự suy Đạt/Không đạt.
        if (!dto.ThresholdOperator.HasValue)
            return BadRequest(new { status = "error", message = "Toán tử ngưỡng (ThresholdOperator) là bắt buộc.", error_code = "THRESHOLD_OPERATOR_REQUIRED" });
        if (!dto.ThresholdValue.HasValue)
            return BadRequest(new { status = "error", message = "Giá trị ngưỡng (ThresholdValue) là bắt buộc.", error_code = "THRESHOLD_VALUE_REQUIRED" });

        var param = new MaintenanceStandardParam
        {
            ChecklistItemId = itemId,
            ParamName = dto.ParamName.Trim(),
            NominalValue = dto.NominalValue,
            ThresholdOperator = dto.ThresholdOperator.Value,
            ThresholdValue = dto.ThresholdValue.Value,
            CheckMethod = dto.CheckMethod,
            Unit = dto.Unit
        };
        _context.MaintenanceStandardParams.Add(param);
        await _context.SaveChangesAsync();

        LogTemplateAction(ActionType.Create, t, $"Thêm tiêu chuẩn \"{param.ParamName}\" vào hạng mục \"{item.Name}\" (version {version.VersionNumber})",
            new { scope = "param", versionId, versionNumber = version.VersionNumber, itemId, paramId = param.Id });
        await _context.SaveChangesAsync();

        return Ok(new
        {
            status = "success",
            data = new { param.Id, param.ParamName, param.NominalValue, param.ThresholdOperator, param.ThresholdValue, param.CheckMethod, param.Unit, itemId }
        });
    }

    [HttpPut("{id:guid}/versions/{versionId:guid}/items/{itemId:guid}/standard-params/{paramId:guid}")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> UpdateParam(Guid id, Guid versionId, Guid itemId, Guid paramId, [FromBody] MaintenanceStandardParamDto dto)
    {
        var t = await GetVisibleTemplateAsync(id);
        if (t == null) return NotFound(new { status = "error", message = "Not found." });
        var version = await GetVersionOfTemplateAsync(t, versionId);
        if (version == null) return NotFound(new { status = "error", message = "Version not found." });
        var param = await _context.MaintenanceStandardParams.FirstOrDefaultAsync(p => p.Id == paramId && p.ChecklistItemId == itemId);
        if (param == null) return NotFound(new { status = "error", message = "Param not found." });
        if (await VersionHasCampaignsAsync(versionId)) return FrozenVersion(version);

        var before = new { param.ParamName, param.NominalValue, param.ThresholdOperator, param.ThresholdValue, param.CheckMethod, param.Unit };
        if (!string.IsNullOrWhiteSpace(dto.ParamName)) param.ParamName = dto.ParamName.Trim();
        if (dto.NominalValue is not null) param.NominalValue = dto.NominalValue;
        // [MC-10] Patch-aware nhưng luôn theo cặp: Operator+Value (cả 2 hoặc không đổi).
        if (dto.ThresholdOperator.HasValue) param.ThresholdOperator = dto.ThresholdOperator.Value;
        if (dto.ThresholdValue.HasValue) param.ThresholdValue = dto.ThresholdValue.Value;
        if (dto.CheckMethod is not null) param.CheckMethod = dto.CheckMethod;
        if (dto.Unit is not null) param.Unit = dto.Unit;
        await _context.SaveChangesAsync();

        LogTemplateAction(ActionType.Update, t, $"Sửa tiêu chuẩn \"{param.ParamName}\" (version {version.VersionNumber})", new
        {
            scope = "param",
            versionId,
            versionNumber = version.VersionNumber,
            itemId,
            paramId,
            changes = new
            {
                paramName = new { old = before.ParamName, @new = param.ParamName },
                nominalValue = new { old = before.NominalValue, @new = param.NominalValue },
                thresholdOperator = new { old = before.ThresholdOperator.ToString(), @new = param.ThresholdOperator.ToString() },
                thresholdValue = new { old = before.ThresholdValue, @new = param.ThresholdValue }
            }
        });
        await _context.SaveChangesAsync();

        return Ok(new { status = "success", message = "Param updated." });
    }

    [HttpDelete("{id:guid}/versions/{versionId:guid}/items/{itemId:guid}/standard-params/{paramId:guid}")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> DeleteParam(Guid id, Guid versionId, Guid itemId, Guid paramId)
    {
        var t = await GetVisibleTemplateAsync(id);
        if (t == null) return NotFound(new { status = "error", message = "Not found." });
        var version = await GetVersionOfTemplateAsync(t, versionId);
        if (version == null) return NotFound(new { status = "error", message = "Version not found." });
        var param = await _context.MaintenanceStandardParams.FirstOrDefaultAsync(p => p.Id == paramId && p.ChecklistItemId == itemId);
        if (param == null) return NotFound(new { status = "error", message = "Param not found." });
        if (await VersionHasCampaignsAsync(versionId)) return FrozenVersion(version);

        var name = param.ParamName;
        _context.MaintenanceStandardParams.Remove(param);
        await _context.SaveChangesAsync();

        LogTemplateAction(ActionType.Delete, t, $"Xóa tiêu chuẩn \"{name}\" khỏi hạng mục (version {version.VersionNumber})",
            new { scope = "param", versionId, versionNumber = version.VersionNumber, itemId, paramId });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Param deleted." });
    }

    private IActionResult FrozenVersion(MaintenanceChecklistTemplateVersion version) => BadRequest(new
    {
        status = "error",
        message = $"Version {version.VersionNumber} đã có đợt bảo dưỡng tham chiếu — nội dung bất biến. Hãy tạo version mới.",
        error_code = "TEMPLATE_VERSION_IN_USE"
    });
}

// ─── DTOs (patch-aware: nullable fields — an ABSENT field must never wipe real data) ───

public record CreateMaintenanceTemplateDto(string? Name, Guid? SystemInfoId, Guid? CompanyId);

public record UpdateMaintenanceTemplateDto(string? Name, Guid? SystemInfoId, Guid? CompanyId, bool? IsActive);

public record CreateTemplateVersionDto(DateTime? EffectiveFrom);

public record UpdateTemplateVersionDto(DateTime? EffectiveFrom);

public record MaintenanceChecklistItemDto(
    int? Order,
    string? Name,
    int? CycleMonths,
    string? ToolsRequired,
    string? Instruction,
    Guid[]? PositionIds = null);

public record MaintenanceStandardParamDto(
    string? ParamName,
    string? NominalValue,
    MaintenanceThresholdOperator? ThresholdOperator = null,
    decimal? ThresholdValue = null,
    string? CheckMethod = null,
    string? Unit = null);
