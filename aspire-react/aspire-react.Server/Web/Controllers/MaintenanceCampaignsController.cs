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
/// MC-3 — Maintenance CAMPAIGN: create (auto device snapshot), checklist results (upsert), complete
/// (Status → Completed + SystemInfo.NextMaintenanceDueDate), campaign-level ActionLogs.
/// <para>
/// Điểm thiết kế đã chốt:
///  - Create: ghim TemplateVersion IsCurrent=true ĐÃ publish của template thuộc SystemInfo; snapshot
///    toàn bộ Asset đang gắn tại các SystemPosition của hệ thống (bất biến — AssetId/SystemPositionId
///    là plain Guid, denormalized text, KHÔNG FK — snapshot sống sót khi asset sau này đổi chỗ/xóa).
///    CompanyId của campaign = CompanyId của SystemInfo (floater = null). Chặn 2 campaign InProgress
///    trên cùng 1 hệ thống (tránh snapshot trùng + đè lịch bảo dưỡng).
///  - Results: upsert theo (DeviceSnapshot, ChecklistItem) unique; patch-aware; chỉ trước khi Complete.
///  - Complete: yêu cầu đủ S×I kết quả; EndDate ??= UtcNow; STATUS → Completed; ReviewerId ??= user;
///    NextMaintenanceDueDate = (EndDate ?? UtcNow) + min(CycleMonths) của MỌI item trong version đã pin
///    (người dùng chốt: cảnh báo SỚM theo hạng mục lặp lại thường xuyên nhất — lý do an toàn hạ tầng).
///  - ActionLog: chỉ Create + Complete (ItemType.MaintenanceCampaign, TargetSystemInfoId đúng, LogMeta
///    chuẩn); KHÔNG log từng kết quả. GetBySystem đã mở rộng filter ở ActionLogsController (hướng b).
/// </summary>
[ApiController]
[Route("api/v1/maintenance/campaigns")]
[Authorize]
public class MaintenanceCampaignsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public MaintenanceCampaignsController(
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

    private Task<Guid?> GetUserCompanyIdAsync() => _companyScope.GetCurrentUserCompanyIdAsync();

    /// <summary>timestamp with time zone columns MUST receive Kind=Utc (DateTime Kind convention).</summary>
    private static DateTime ToUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    /// <summary>[MC-10] Trích số thập phân đầu tiên từ chuỗi đo ("55%" → 55, "12,5" → 12.5, "-3" → -3).</summary>
    private static bool TryParseMeasured(string? raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var m = System.Text.RegularExpressions.Regex.Match(raw.Trim(), @"-?\d+(?:[.,]\d+)?");
        if (!m.Success) return false;
        return decimal.TryParse(m.Value.Replace(',', '.'), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    /// <summary>[MC-10] Đánh giá Đạt/Không đạt theo toán tử ngưỡng.</summary>
    private static bool EvaluateThreshold(MaintenanceThresholdOperator op, decimal threshold, decimal measured)
        => op switch
        {
            MaintenanceThresholdOperator.LessThan => measured < threshold,
            MaintenanceThresholdOperator.LessOrEqual => measured <= threshold,
            MaintenanceThresholdOperator.GreaterThan => measured > threshold,
            MaintenanceThresholdOperator.GreaterOrEqual => measured >= threshold,
            _ => Math.Abs(measured - threshold) < 0.0001m // Equal
        };

    /// <summary>Campaign lookup + company visibility (floater/own-company; superuser sees all).</summary>
    private async Task<MaintenanceCampaign?> GetVisibleCampaignAsync(Guid id)
    {
        var userCompanyId = await GetUserCompanyIdAsync();
        var c = await _context.MaintenanceCampaigns
            .Include(x => x.SystemInfo)
            .Include(x => x.TemplateVersion)
            .Include(x => x.DeviceSnapshots)
            .Include(x => x.Results)
            .Include(x => x.Executors).ThenInclude(e => e.User)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return null;
        if (userCompanyId.HasValue && c.CompanyId.HasValue && c.CompanyId.Value != userCompanyId.Value)
            return null;
        return c;
    }

    private void LogCampaignAction(ActionType actionType, MaintenanceCampaign campaign, string note, object? meta = null)
    {
        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.MaintenanceCampaign,
            ItemId = campaign.Id,
            ActionType = actionType,
            CreatedBy = GetCurrentUserId(),
            CompanyId = campaign.CompanyId,
            TargetSystemInfoId = campaign.SystemInfoId,
            TargetSystemInfoName = campaign.SystemInfo?.Name,
            LogMeta = meta == null ? null : JsonSerializer.Serialize(meta),
            Note = note
        });
    }

    /// <summary>Resolves the template + current published version to pin, or a typed 400/404 why not.</summary>
    private async Task<(MaintenanceChecklistTemplate? template, MaintenanceChecklistTemplateVersion? version, IActionResult? error)>
        ResolvePinableVersionAsync(Guid systemInfoId, Guid? templateId)
    {
        var userCompanyId = await GetUserCompanyIdAsync();

        var template = await _context.MaintenanceChecklistTemplates
            .Include(t => t.Versions)
            .FirstOrDefaultAsync(t => t.Id == templateId.GetValueOrDefault());

        if (templateId.HasValue)
        {
            if (template == null)
                return (null, null, NotFound(new { status = "error", message = "Template not found." }));
            if (userCompanyId.HasValue && template.CompanyId.HasValue && template.CompanyId.Value != userCompanyId.Value)
                return (null, null, NotFound(new { status = "error", message = "Template not found." }));
            if (template.SystemInfoId != systemInfoId)
                return (null, null, BadRequest(new
                {
                    status = "error",
                    message = "Template không thuộc hệ thống đã chọn.",
                    error_code = "TEMPLATE_SYSTEM_MISMATCH"
                }));
        }
        else
        {
            var templates = await _context.MaintenanceChecklistTemplates.AsNoTracking()
                .Where(t => t.SystemInfoId == systemInfoId &&
                            (!userCompanyId.HasValue || t.CompanyId == null || t.CompanyId == userCompanyId.Value))
                .ToListAsync();
            if (templates.Count == 0)
                return (null, null, BadRequest(new { status = "error", message = "Hệ thống chưa có template bảo dưỡng.", error_code = "NO_TEMPLATE" }));
            if (templates.Count > 1)
                return (null, null, BadRequest(new
                {
                    status = "error",
                    message = "Hệ thống có nhiều template — cần chỉ định templateId.",
                    error_code = "AMBIGUOUS_TEMPLATE"
                }));
            template = await _context.MaintenanceChecklistTemplates
                .Include(t => t.Versions)
                .FirstAsync(t => t.Id == templates[0].Id);
        }

        var current = template.Versions.FirstOrDefault(v => v.IsCurrent);
        if (current == null || !current.PublishedAt.HasValue)
            return (null, null, BadRequest(new
            {
                status = "error",
                message = "Template chưa có version hiện hành đã publish — hãy publish trước.",
                error_code = "NO_CURRENT_VERSION"
            }));

        return (template, current, null);
    }

    // ==================== List / Detail (maintenance.view) ====================

    [HttpGet]
    [Authorize(Policy = "maintenance.view")]
    public async Task<IActionResult> GetAll([FromQuery] Guid? systemInfoId)
    {
        var userCompanyId = await GetUserCompanyIdAsync();
        var query = _context.MaintenanceCampaigns.AsNoTracking();
        if (userCompanyId.HasValue)
            query = query.Where(c => c.CompanyId == null || c.CompanyId == userCompanyId.Value);
        if (systemInfoId.HasValue && systemInfoId.Value != Guid.Empty)
            query = query.Where(c => c.SystemInfoId == systemInfoId.Value);

        var list = await query
            .OrderByDescending(c => c.StartDate)
            .Select(c => new
            {
                c.Id,
                c.SystemInfoId,
                SystemInfoName = c.SystemInfo.Name,
                c.TemplateVersionId,
                VersionNumber = c.TemplateVersion.VersionNumber,
                c.StartDate,
                c.EndDate,
                c.BatchNumber,
                c.CompanyId,
                c.ReviewerId,
                Status = c.Status.ToString(),
                c.CreatedAt,
                SnapshotCount = c.DeviceSnapshots.Count(),
                ResultsCount = c.Results.Count()
            })
            .ToListAsync();
        return Ok(new { status = "success", data = list });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "maintenance.view")]
    public async Task<IActionResult> Get(Guid id)
    {
        var c = await GetVisibleCampaignAsync(id);
        if (c == null) return NotFound(new { status = "error", message = "Not found." });

        var data = new
        {
            c.Id,
            c.SystemInfoId,
            SystemInfoName = c.SystemInfo.Name,
            c.TemplateVersionId,
            // [MC-6] Frontend cần templateId để lấy items của version đã pin (version detail endpoint).
            TemplateId = c.TemplateVersion.TemplateId,
            VersionNumber = c.TemplateVersion.VersionNumber,
            c.StartDate,
            c.EndDate,
            c.BatchNumber,
            c.CompanyId,
            c.ReviewerId,
            Status = c.Status.ToString(),
            c.CreatedAt,
            Executors = c.Executors.Select(e => new
            {
                e.UserId,
                FullName = ((e.User.FirstName ?? "") + " " + (e.User.LastName ?? "")).Trim() is { Length: > 0 } n ? n : e.User.Username
            }),
            Snapshots = c.DeviceSnapshots.OrderBy(s => s.SystemPositionName).ThenBy(s => s.AssetTag).Select(s => new
            {
                s.Id,
                s.AssetId,
                s.AssetTag,
                s.AssetName,
                s.Serial,
                s.ModelNumber,
                s.SystemPositionId,
                s.SystemPositionName
            }),
            Results = c.Results.Select(r => new
            {
                r.Id,
                r.DeviceSnapshotId,
                r.ChecklistItemId,
                r.StandardParamId,
                r.MeasuredValue,
                r.IsPass,
                r.Notes
            })
        };
        return Ok(new { status = "success", data });
    }

    // ==================== Create (maintenance.campaigns) ====================

    [HttpPost]
    [Authorize(Policy = "maintenance.campaigns")]
    public async Task<IActionResult> Create([FromBody] CreateCampaignRequest dto)
    {
        if (dto.SystemInfoId == Guid.Empty)
            return BadRequest(new { status = "error", message = "Hệ thống (SystemInfoId) là bắt buộc.", error_code = "SYSTEM_INFO_REQUIRED" });

        // Company scope on the system (hide existence out-of-scope).
        var userCompanyId = await GetUserCompanyIdAsync();
        var sys = await _context.SystemInfos.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == dto.SystemInfoId);
        if (sys == null)
            return NotFound(new { status = "error", message = "Hệ thống không tồn tại hoặc ngoài phạm vi công ty của bạn." });
        if (userCompanyId.HasValue && sys.CompanyId.HasValue && sys.CompanyId.Value != userCompanyId.Value)
            return NotFound(new { status = "error", message = "Hệ thống không tồn tại hoặc ngoài phạm vi công ty của bạn." });

        if (dto.EndDate.HasValue && dto.StartDate.HasValue && dto.EndDate.Value < dto.StartDate.Value)
            return BadRequest(new { status = "error", message = "Ngày kết thúc không được trước ngày bắt đầu.", error_code = "END_BEFORE_START" });

        // One InProgress campaign per system at a time (no duplicate concurrent snapshots, no due-date races).
        if (await _context.MaintenanceCampaigns.AnyAsync(c => c.SystemInfoId == dto.SystemInfoId && c.Status == MaintenanceCampaignStatus.InProgress))
            return BadRequest(new
            {
                status = "error",
                message = "Hệ thống đang có một đợt bảo dưỡng chưa hoàn thành — hãy hoàn thành hoặc chờ nó kết thúc.",
                error_code = "CAMPAIGN_ALREADY_IN_PROGRESS"
            });

        var (template, version, error) = await ResolvePinableVersionAsync(dto.SystemInfoId, dto.TemplateId);
        if (error != null) return error;

        if (dto.ReviewerId.HasValue && !await _context.Users.AsNoTracking().AnyAsync(u => u.Id == dto.ReviewerId.Value))
            return BadRequest(new { status = "error", message = "Người duyệt không tồn tại.", error_code = "INVALID_REVIEWER" });

        // [MC-6] Executor UI (hoãn từ MC-3): nhiều người thực hiện. Mirror ValidateAssigneesAsync
        // (AssetMaintenancesController): distinct, tồn tại, cùng công ty với hệ thống.
        var executorIds = dto.ExecutorIds?.Distinct().ToArray() ?? Array.Empty<Guid>();
        if (executorIds.Length > 0)
        {
            var executors = await _context.Users.AsNoTracking()
                .Where(u => executorIds.Contains(u.Id))
                .Select(u => new { u.Id, u.CompanyId })
                .ToListAsync();
            if (executors.Count != executorIds.Length)
                return BadRequest(new { status = "error", message = "Có người thực hiện không tồn tại trong hệ thống.", error_code = "INVALID_EXECUTOR" });
            if (userCompanyId.HasValue && sys.CompanyId != null && sys.CompanyId.Value != Guid.Empty
                && executors.Any(u => u.CompanyId != sys.CompanyId))
                return BadRequest(new { status = "error", message = "Người thực hiện phải thuộc cùng công ty với hệ thống.", error_code = "EXECUTOR_COMPANY_MISMATCH" });
        }

        var userId = GetCurrentUserId();
        var startDate = dto.StartDate.HasValue ? ToUtc(dto.StartDate.Value) : DateTime.UtcNow;
        var endDate = dto.EndDate.HasValue ? ToUtc(dto.EndDate.Value) : (DateTime?)null;

        var campaign = new MaintenanceCampaign
        {
            SystemInfoId = dto.SystemInfoId,
            TemplateVersionId = version!.Id,
            StartDate = startDate,
            EndDate = endDate,
            BatchNumber = string.IsNullOrWhiteSpace(dto.BatchNumber) ? null : dto.BatchNumber.Trim(),
            CompanyId = sys.CompanyId, // server-set from SystemInfo (floater = null)
            ReviewerId = dto.ReviewerId,
            Status = MaintenanceCampaignStatus.InProgress
        };

        // ── Snapshot ALL assets currently mounted at the system's positions (immutable copy). ──
        var mountedAssets = await _context.Assets.AsNoTracking()
            .Include(a => a.Model)
            .Include(a => a.SystemPosition)
            .Where(a => a.SystemPositionId != null && a.SystemPosition!.SystemInfoId == dto.SystemInfoId)
            .ToListAsync();

        foreach (var a in mountedAssets)
        {
            campaign.DeviceSnapshots.Add(new MaintenanceCampaignDeviceSnapshot
            {
                AssetId = a.Id,
                AssetTag = a.AssetTag,
                AssetName = a.Name,
                Serial = a.Serial,
                ModelNumber = a.Model?.ModelNumber,
                SystemPositionId = a.SystemPositionId,
                SystemPositionName = a.SystemPosition?.Name
            });
        }

        // [MC-6] Persist executors (nhiều người thực hiện — join bảng MaintenanceCampaignExecutor).
        foreach (var uid in executorIds)
        {
            campaign.Executors.Add(new MaintenanceCampaignExecutor { UserId = uid });
        }

        _context.MaintenanceCampaigns.Add(campaign);
        await _context.SaveChangesAsync();

        LogCampaignAction(ActionType.Create, campaign,
            $"Tạo đợt bảo dưỡng cho hệ thống \"{sys.Name}\" — version {version.VersionNumber}",
            new
            {
                templateVersionId = version.Id,
                versionNumber = version.VersionNumber,
                batchNumber = campaign.BatchNumber,
                startDate = campaign.StartDate,
                endDate = campaign.EndDate,
                snapshotCount = campaign.DeviceSnapshots.Count,
                executorIds = executorIds
            });
        await _context.SaveChangesAsync();

        return Ok(new
        {
            status = "success",
            data = new
            {
                campaign.Id,
                campaign.SystemInfoId,
                campaign.TemplateVersionId,
                VersionNumber = version.VersionNumber,
                campaign.StartDate,
                campaign.EndDate,
                campaign.BatchNumber,
                campaign.CompanyId,
                campaign.ReviewerId,
                Status = campaign.Status.ToString(),
                SnapshotCount = campaign.DeviceSnapshots.Count,
                ExecutorCount = executorIds.Length
            }
        });
    }

    // ==================== Results (maintenance.campaigns, chỉ khi InProgress) ====================

    /// <summary>
    /// [MC-7c] Cặp (item, snapshot) có thuộc phạm vi áp dụng không: item KHÔNG khai báo vị trí
    /// (universal) → áp dụng mọi snapshot; item có khai báo → snapshot.SystemPositionId phải ∈ danh sách.
    /// </summary>
    private async Task<bool> IsApplicablePairAsync(Guid itemId, Guid? snapshotSystemPositionId)
    {
        var declared = await _context.MaintenanceChecklistItemPositions.AsNoTracking()
            .Where(ip => ip.ItemId == itemId)
            .Select(ip => ip.SystemPositionId)
            .ToListAsync();
        if (declared.Count == 0) return true; // universal
        return snapshotSystemPositionId.HasValue && declared.Contains(snapshotSystemPositionId.Value);
    }

    /// <summary>Upsert 1 kết quả (DeviceSnapshot × ChecklistItem × StandardParam?). Patch-aware: field không gửi không ghi đè. [MC-9] mỗi tiêu chuẩn có 1 dòng riêng; NULL = hạng mục không có tiêu chuẩn.</summary>
    [HttpPost("{id:guid}/results")]
    [Authorize(Policy = "maintenance.campaigns")]
    public async Task<IActionResult> UpsertResult(Guid id, [FromBody] UpsertCampaignResultDto dto)
    {
        var c = await GetVisibleCampaignAsync(id);
        if (c == null) return NotFound(new { status = "error", message = "Not found." });
        if (c.Status == MaintenanceCampaignStatus.Completed)
            return BadRequest(new { status = "error", message = "Đợt bảo dưỡng đã hoàn thành — không thể sửa kết quả.", error_code = "CAMPAIGN_COMPLETED" });
        if (dto.DeviceSnapshotId == Guid.Empty || dto.ChecklistItemId == Guid.Empty)
            return BadRequest(new { status = "error", message = "Cần chỉ định deviceSnapshotId và checklistItemId.", error_code = "RESULT_TARGET_REQUIRED" });

        // Snapshot must belong to THIS campaign.
        if (!c.DeviceSnapshots.Any(s => s.Id == dto.DeviceSnapshotId))
            return BadRequest(new { status = "error", message = "DeviceSnapshot không thuộc đợt bảo dưỡng này.", error_code = "INVALID_DEVICE_SNAPSHOT" });
        // Item must belong to the pinned template version.
        if (!await _context.MaintenanceChecklistItems.AnyAsync(i => i.Id == dto.ChecklistItemId && i.TemplateVersionId == c.TemplateVersionId))
            return BadRequest(new { status = "error", message = "Hạng mục không thuộc checklist của version đã pin.", error_code = "INVALID_CHECKLIST_ITEM" });

        // [MC-7c] Chặn cặp ngoài phạm vi: Item khai báo vị trí áp dụng, nhưng thiết bị (snapshot) không ở
        // vị trí đó → 400 INVALID_ITEM_POSITION (không cho tạo result thừa ngay từ upsert).
        var snapshotPosId = c.DeviceSnapshots.FirstOrDefault(s => s.Id == dto.DeviceSnapshotId)?.SystemPositionId;
        if (!await IsApplicablePairAsync(dto.ChecklistItemId, snapshotPosId))
            return BadRequest(new
            {
                status = "error",
                message = "Hạng mục này chỉ áp dụng cho các vị trí đã khai báo trong template — thiết bị này không thuộc phạm vi.",
                error_code = "INVALID_ITEM_POSITION"
            });

        // [MC-9] Validate StandardParamId: nếu gửi thì phải thuộc ChecklistItem đó; nếu không gửi thì phải là
        // hạng mục KHÔNG có tiêu chuẩn nào (để không lẫn lộn).
        var paramCount = await _context.MaintenanceStandardParams.CountAsync(p => p.ChecklistItemId == dto.ChecklistItemId);
        if (paramCount == 0 && dto.StandardParamId.HasValue)
            return BadRequest(new { status = "error", message = "Hạng mục này không có tiêu chuẩn kỹ thuật — không cần StandardParamId.", error_code = "STANDARD_PARAM_NOT_APPLICABLE" });
        if (paramCount > 0 && !dto.StandardParamId.HasValue)
            return BadRequest(new { status = "error", message = "Hạng mục này có tiêu chuẩn kỹ thuật — cần chỉ định StandardParamId.", error_code = "STANDARD_PARAM_REQUIRED" });
        if (dto.StandardParamId.HasValue)
        {
            var belongs = await _context.MaintenanceStandardParams.AnyAsync(p => p.Id == dto.StandardParamId.Value && p.ChecklistItemId == dto.ChecklistItemId);
            if (!belongs)
                return BadRequest(new { status = "error", message = "Tiêu chuẩn không thuộc hạng mục đã chọn.", error_code = "INVALID_STANDARD_PARAM" });
        }

        var existing = await _context.MaintenanceChecklistResults
            .FirstOrDefaultAsync(r => r.CampaignId == id && r.DeviceSnapshotId == dto.DeviceSnapshotId && r.ChecklistItemId == dto.ChecklistItemId && r.StandardParamId == dto.StandardParamId);

        // [BUG-B fix] isNew phân biệt Create (lần đầu ghi cặp key) vs Update (ghi đè) cho ActionLog.
        // Giá trị cũ được chụp TRƯỚC khi patch để LogMeta.changes phản ánh đúng old→new.
        // oldIsPass là bool? — Create-case KHÔNG có giá trị cũ → old phải null trong LogMeta,
        // KHÔNG được mặc định false (false là giá trị đo có nghĩa: "không đạt").
        var isNew = false;
        string? oldMeasuredValue = null;
        bool? oldIsPass = null;
        string? oldNotes = null;
        if (existing == null)
        {
            isNew = true;
            existing = new MaintenanceChecklistResult
            {
                CampaignId = id,
                DeviceSnapshotId = dto.DeviceSnapshotId,
                ChecklistItemId = dto.ChecklistItemId,
                StandardParamId = dto.StandardParamId,
                MeasuredValue = dto.MeasuredValue,
                IsPass = dto.IsPass ?? false,
                Notes = dto.Notes
            };
            _context.MaintenanceChecklistResults.Add(existing);
        }
        else
        {
            // Patch semantics (Task F/M1): absent field NEVER overwrites existing data.
            oldMeasuredValue = existing.MeasuredValue;
            oldIsPass = existing.IsPass;
            oldNotes = existing.Notes;
            if (dto.MeasuredValue is not null) existing.MeasuredValue = dto.MeasuredValue;
            if (dto.IsPass.HasValue) existing.IsPass = dto.IsPass.Value;
            if (dto.Notes is not null) existing.Notes = dto.Notes;
        }

        // [MC-10] Dòng gắn StandardParam → IsPass TỰ ĐỘNG = so sánh(MeasuredValue, Threshold) theo Operator.
        // Không tin client gửi isPass (máy quyết định thay — đúng thiết kế đã chốt hướng a).
        if (dto.StandardParamId.HasValue)
        {
            var param = await _context.MaintenanceStandardParams.AsNoTracking()
                .FirstAsync(p => p.Id == dto.StandardParamId.Value);
            existing.IsPass = TryParseMeasured(existing.MeasuredValue, out var mv)
                ? EvaluateThreshold(param.ThresholdOperator, param.ThresholdValue, mv)
                : false; // chưa có giá trị đo → chưa xác định (UI hiện "Chưa xác định" dựa trên MeasuredValue rỗng)
        }

        await _context.SaveChangesAsync();

        // [BUG-B fix] ActionLog cho ghi/xóa kết quả checklist — cùng format LogCampaignAction
        // (ItemType.MaintenanceCampaign + TargetSystemInfo) như Create/Complete ở trên.
        // Auto-IsPass là giá trị máy tính (post-MC-10), đo được trong LogMeta để truy vết.
        LogCampaignAction(isNew ? ActionType.Create : ActionType.Update, c,
            isNew
                ? $"Ghi kết quả checklist đợt \"{(c.SystemInfo?.Name ?? c.SystemInfoId.ToString())}\""
                : $"Cập nhật kết quả checklist đợt \"{(c.SystemInfo?.Name ?? c.SystemInfoId.ToString())}\"",
            new
            {
                campaignId = c.Id,
                deviceSnapshotId = dto.DeviceSnapshotId,
                checklistItemId = dto.ChecklistItemId,
                standardParamId = dto.StandardParamId,
                changes = new
                {
                    measuredValue = new { old = oldMeasuredValue, @new = existing.MeasuredValue },
                    isPass = new { old = oldIsPass, @new = existing.IsPass },
                    notes = new { old = oldNotes, @new = existing.Notes }
                }
            });
        await _context.SaveChangesAsync();

        return Ok(new
        {
            status = "success",
            data = new { existing.Id, existing.DeviceSnapshotId, existing.ChecklistItemId, existing.StandardParamId, existing.MeasuredValue, existing.IsPass, existing.Notes }
        });
    }

    [HttpDelete("{id:guid}/results")]
    [Authorize(Policy = "maintenance.campaigns")]
    public async Task<IActionResult> DeleteResult(Guid id, [FromBody] DeleteCampaignResultDto dto)
    {
        var c = await GetVisibleCampaignAsync(id);
        if (c == null) return NotFound(new { status = "error", message = "Not found." });
        if (c.Status == MaintenanceCampaignStatus.Completed)
            return BadRequest(new { status = "error", message = "Đợt bảo dưỡng đã hoàn thành — không thể sửa kết quả.", error_code = "CAMPAIGN_COMPLETED" });

        var result = await _context.MaintenanceChecklistResults
            .FirstOrDefaultAsync(r => r.CampaignId == id && r.DeviceSnapshotId == dto.DeviceSnapshotId && r.ChecklistItemId == dto.ChecklistItemId && r.StandardParamId == dto.StandardParamId);
        if (result == null) return NotFound(new { status = "error", message = "Kết quả không tồn tại." });

        _context.MaintenanceChecklistResults.Remove(result);
        await _context.SaveChangesAsync();

        // [BUG-B fix] ActionLog cho xóa kết quả checklist — đủ dữ liệu truy vết bản ghi đã xóa.
        LogCampaignAction(ActionType.Delete, c,
            $"Xóa kết quả checklist đợt \"{(c.SystemInfo?.Name ?? c.SystemInfoId.ToString())}\"",
            new
            {
                campaignId = c.Id,
                deviceSnapshotId = dto.DeviceSnapshotId,
                checklistItemId = dto.ChecklistItemId,
                standardParamId = dto.StandardParamId,
                deleted = new { measuredValue = result.MeasuredValue, isPass = result.IsPass, notes = result.Notes }
            });
        await _context.SaveChangesAsync();

        return Ok(new { status = "success", message = "Result deleted." });
    }

    // ==================== Complete (maintenance.campaigns) ====================

    [HttpPost("{id:guid}/complete")]
    [Authorize(Policy = "maintenance.campaigns")]
    public async Task<IActionResult> Complete(Guid id)
    {
        var c = await GetVisibleCampaignAsync(id);
        if (c == null) return NotFound(new { status = "error", message = "Not found." });
        if (c.Status == MaintenanceCampaignStatus.Completed)
            return BadRequest(new { status = "error", message = "Đợt bảo dưỡng đã hoàn thành.", error_code = "CAMPAIGN_ALREADY_COMPLETED" });

        // ── Completeness gate: mọi CẶP ÁP DỤNG (item × snapshot × param) phải có kết quả.
        // [MC-7c] KHÔNG còn S×I toàn phần: item không khai báo vị trí (universal) → đếm mọi snapshot;
        // item khai báo vị trí → chỉ đếm snapshot nằm trong danh sách vị trí của item.
        // [MC-9] Với hạng mục CÓ tiêu chuẩn: số dòng = snapshots_applicable × paramCount; KHÔNG có tiêu chuẩn: ×1.
        var snapshots = c.DeviceSnapshots.ToList();
        var items = await _context.MaintenanceChecklistItems.AsNoTracking()
            .Include(i => i.Positions)
            .Include(i => i.StandardParams)
            .Where(i => i.TemplateVersionId == c.TemplateVersionId)
            .ToListAsync();
        var resultCount = c.Results.Count;
        var expected = items.Sum(it =>
        {
            var applicableSnapshots = it.Positions.Count == 0
                ? snapshots.Count
                : snapshots.Count(s => s.SystemPositionId.HasValue && it.Positions.Any(p => p.SystemPositionId == s.SystemPositionId.Value));
            var factor = it.StandardParams.Count == 0 ? 1 : it.StandardParams.Count;
            return applicableSnapshots * factor;
        });
        if (expected > 0 && resultCount < expected)
            return BadRequest(new
            {
                status = "error",
                message = $"Cần ghi đủ kết quả checklist trước khi hoàn thành ({resultCount}/{expected} bản ghi).",
                error_code = "CAMPAIGN_RESULTS_INCOMPLETE"
            });

        var endDate = c.EndDate ?? DateTime.UtcNow;
        var prevDue = c.SystemInfo?.NextMaintenanceDueDate;

        c.EndDate = endDate;
        c.Status = MaintenanceCampaignStatus.Completed;
        c.ReviewerId ??= GetCurrentUserId();

        // ── NextMaintenanceDueDate = EndDate + min(CycleMonths) over ALL items of the pinned version.
        // User-confirmed (MC-3): warn EARLY — the most frequent checklist item drives the next due date.
        DateTime? due = items.Count > 0
            ? endDate.AddMonths(items.Min(i => i.CycleMonths))
            : null;

        if (c.SystemInfo == null)
            c.SystemInfo = await _context.SystemInfos.FindAsync(c.SystemInfoId);
        if (c.SystemInfo != null) c.SystemInfo.NextMaintenanceDueDate = due;

        await _context.SaveChangesAsync();

        LogCampaignAction(ActionType.Complete, c, $"Hoàn thành đợt bảo dưỡng cho hệ thống \"{c.SystemInfo?.Name ?? c.SystemInfoId.ToString()}\"", new
        {
            changes = new
            {
                status = new { old = MaintenanceCampaignStatus.InProgress.ToString(), @new = MaintenanceCampaignStatus.Completed.ToString() },
                endDate = new { old = (DateTime?)null, @new = endDate },
                nextMaintenanceDueDate = new { old = prevDue, @new = due },
                reviewerId = new { old = (Guid?)null, @new = c.ReviewerId }
            }
        });
        await _context.SaveChangesAsync();

        return Ok(new
        {
            status = "success",
            data = new
            {
                c.Id,
                Status = c.Status.ToString(),
                c.EndDate,
                NextMaintenanceDueDate = due,
                c.ReviewerId,
                ResultsCount = resultCount
            }
        });
    }
}

public record CreateCampaignRequest(
    Guid SystemInfoId,
    Guid? TemplateId,
    DateTime? StartDate,
    DateTime? EndDate,
    string? BatchNumber,
    Guid? ReviewerId,
    Guid[]? ExecutorIds = null);

public record UpsertCampaignResultDto(
    Guid DeviceSnapshotId,
    Guid ChecklistItemId,
    string? MeasuredValue,
    bool? IsPass,
    string? Notes,
    Guid? StandardParamId = null);

public record DeleteCampaignResultDto(Guid DeviceSnapshotId, Guid ChecklistItemId, Guid? StandardParamId = null);