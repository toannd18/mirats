using aspire_react.Server.Application.MaintenanceTemplates;
using aspire_react.Server.Application.MaintenanceTemplates.Commands;
using aspire_react.Server.Application.MaintenanceTemplates.Queries;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// MC-2 — Maintenance checklist TEMPLATE CRUD + version lifecycle (draft → publish) + items/standard-params.
/// <para>
/// [Giai đoạn 3 — Rất nặng] MediatR migration: TOÀN BỘ 16 endpoint là thin IMediator.Send mapping
/// (3 queries + 13 commands). Guards/whitelist/scope verbatim trong handlers. Log path giữ MANUAL
/// IActionLogService.Log(entry) trong handlers (TargetSystemInfoId/Name + LogMeta default-options
/// — path LogAction của ActionLogBehavior không express được; Publish còn sở hữu transaction riêng
/// nên KHÔNG dùng ILoggableCommand — precedent Components/Consumables/Licenses).
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
    private readonly IMediator _mediator;
    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public MaintenanceTemplatesController(
        IMediator mediator,
        AppDbContext context,
        ICurrentUserService currentUserService,
        ICompanyScopeService companyScope,
        IActionLogService actionLogService)
    {
        _mediator = mediator;
        _context = context;
        _currentUserService = currentUserService;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    private Guid GetCurrentUserId() => _currentUserService.GetLocalUserId();

    // _context/_companyScope/_actionLogService không còn dùng trong controller nhưng GIỮ ctor
    // signature (tests + DI construct đủ 5 tham số; giữ là chi phí zero, xóa là churn tests).

    // ==================== Template CRUD ====================

    [HttpGet]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> GetAll([FromQuery] Guid? systemInfoId)
    {
        var result = await _mediator.Send(new ListMaintenanceTemplatesQuery(systemInfoId));
        return Ok(new { status = "success", data = result.Items });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> Get(Guid id)
    {
        var result = await _mediator.Send(new GetMaintenanceTemplateByIdQuery(id));
        if (!result.Success)
            return NotFound(new { status = "error", message = "Not found." });

        var d = result.Detail!;
        return Ok(new
        {
            status = "success",
            data = new
            {
                d.Id,
                d.Name,
                d.IsActive,
                d.CompanyId,
                Company = d.Company == null ? null : new { d.Company.Id, d.Company.Name },
                SystemInfo = new
                {
                    d.SystemInfo.Id,
                    d.SystemInfo.Code,
                    d.SystemInfo.Name,
                    // [MC-7d] Vị trí của hệ thống template — nguồn options cho multi-select vị trí áp dụng
                    // của hạng mục (cùng policy maintenance.templates, không phụ thuộc systems.view).
                    Positions = d.SystemInfo.Positions
                },
                Versions = d.Versions
            }
        });
    }

    [HttpPost]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> Create([FromBody] CreateMaintenanceTemplateDto dto)
    {
        var result = await _mediator.Send(new CreateMaintenanceTemplateCommand(
            dto.Name, dto.SystemInfoId, dto.CompanyId, GetCurrentUserId()));

        if (!result.Success)
        {
            if (result.ErrorCode == "NOT_FOUND")
                return NotFound(new { status = "error", message = "Hệ thống không tồn tại hoặc ngoài phạm vi công ty của bạn." });
            return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        }

        return Ok(new
        {
            status = "success",
            data = new
            {
                // Key "id" verbatim (old: template.Id) — NOT result.TemplateId which would serialize "templateId".
                Id = result.TemplateId,
                result.Name,
                result.SystemInfoId,
                result.CompanyId,
                result.IsActive,
                result.InitialVersionId
            }
        });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateMaintenanceTemplateDto dto)
    {
        var result = await _mediator.Send(new UpdateMaintenanceTemplateCommand(
            id, dto.Name, dto.SystemInfoId, dto.CompanyId, dto.IsActive, GetCurrentUserId()));

        if (!result.Success)
            return NotFoundOrBadRequest(result.ErrorCode, result.Message);
        return Ok(new { status = "success", message = result.Message });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await _mediator.Send(new DeleteMaintenanceTemplateCommand(id, GetCurrentUserId()));

        if (!result.Success)
            return NotFoundOrBadRequest(result.ErrorCode, result.Message);
        return Ok(new { status = "success", message = result.Message });
    }

    // ==================== Versions ====================

    /// <summary>Tạo một version DRAFT mới (VersionNumber tự tăng). Chưa publish, chưa current.</summary>
    [HttpPost("{id:guid}/versions")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> CreateVersion(Guid id, [FromBody] CreateTemplateVersionDto? dto)
    {
        var result = await _mediator.Send(new CreateTemplateVersionCommand(id, dto?.EffectiveFrom, GetCurrentUserId()));

        if (!result.Success)
            return NotFoundOrBadRequest(result.ErrorCode, result.Message);
        return Ok(new { status = "success", data = result.Version });
    }

    /// <summary>Chi tiết 1 version: đầy đủ items + standard params (đã sort).</summary>
    [HttpGet("{id:guid}/versions/{versionId:guid}")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> GetVersion(Guid id, Guid versionId)
    {
        var result = await _mediator.Send(new GetMaintenanceTemplateVersionQuery(id, versionId));

        if (!result.Success)
        {
            // "Not found." cho template ẩn; "Version not found." cho version — verbatim từng message.
            return NotFound(new { status = "error", message = result.ErrorCode == "VERSION_NOT_FOUND" ? "Version not found." : "Not found." });
        }

        return Ok(new { status = "success", data = result.Detail });
    }

    /// <summary>
    /// Publish: draft → hiện hành. Set PublishedAt=UtcNow + IsCurrent=true, các version khác chuyển
    /// IsCurrent=false (không xóa). Idempotency guard: publish lại version đã publish → VERSION_ALREADY_PUBLISHED.
    /// </summary>
    [HttpPost("{id:guid}/versions/{versionId:guid}/publish")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> PublishVersion(Guid id, Guid versionId)
    {
        var result = await _mediator.Send(new PublishTemplateVersionCommand(id, versionId, GetCurrentUserId()));

        if (!result.Success)
            return NotFoundOrBadRequest(result.ErrorCode, result.Message);
        return Ok(new { status = "success", data = result.Version });
    }

    /// <summary>Sửa metadata version (hiện chỉ EffectiveFrom). Version đã có campaign → TEMPLATE_VERSION_IN_USE.</summary>
    [HttpPut("{id:guid}/versions/{versionId:guid}")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> UpdateVersion(Guid id, Guid versionId, [FromBody] UpdateTemplateVersionDto dto)
    {
        var result = await _mediator.Send(new UpdateTemplateVersionCommand(id, versionId, dto.EffectiveFrom, GetCurrentUserId()));

        if (!result.Success)
            return NotFoundOrBadRequest(result.ErrorCode, result.Message);
        return Ok(new { status = "success", data = result.Version });
    }

    /// <summary>Xóa version CHƯA publish. Đã publish → VERSION_ALREADY_PUBLISHED; có campaign → TEMPLATE_VERSION_IN_USE.</summary>
    [HttpDelete("{id:guid}/versions/{versionId:guid}")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> DeleteVersion(Guid id, Guid versionId)
    {
        var result = await _mediator.Send(new DeleteTemplateVersionCommand(id, versionId, GetCurrentUserId()));

        if (!result.Success)
            return NotFoundOrBadRequest(result.ErrorCode, result.Message);
        return Ok(new { status = "success", message = result.Message });
    }

    // ==================== Checklist Items ====================

    [HttpPost("{id:guid}/versions/{versionId:guid}/items")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> AddItem(Guid id, Guid versionId, [FromBody] MaintenanceChecklistItemDto dto)
    {
        var result = await _mediator.Send(new AddChecklistItemCommand(
            id, versionId, dto.Order, dto.Name, dto.CycleMonths, dto.ToolsRequired, dto.Instruction,
            dto.PositionIds, GetCurrentUserId()));

        if (!result.Success)
            return NotFoundOrBadRequest(result.ErrorCode, result.Message);
        return Ok(new
        {
            status = "success",
            data = new
            {
                // Key "id" verbatim (old: item.Id).
                Id = result.ItemId,
                result.Order,
                result.Name,
                result.CycleMonths,
                result.ToolsRequired,
                result.Instruction,
                result.PositionIds
            }
        });
    }

    [HttpPut("{id:guid}/versions/{versionId:guid}/items/{itemId:guid}")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> UpdateItem(Guid id, Guid versionId, Guid itemId, [FromBody] MaintenanceChecklistItemDto dto)
    {
        var result = await _mediator.Send(new UpdateChecklistItemCommand(
            id, versionId, itemId, dto.Order, dto.Name, dto.CycleMonths, dto.ToolsRequired, dto.Instruction,
            dto.PositionIds, GetCurrentUserId()));

        if (!result.Success)
            return NotFoundOrBadRequest(result.ErrorCode, result.Message);
        return Ok(new { status = "success", message = result.Message });
    }

    [HttpDelete("{id:guid}/versions/{versionId:guid}/items/{itemId:guid}")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> DeleteItem(Guid id, Guid versionId, Guid itemId)
    {
        var result = await _mediator.Send(new DeleteChecklistItemCommand(id, versionId, itemId, GetCurrentUserId()));

        if (!result.Success)
            return NotFoundOrBadRequest(result.ErrorCode, result.Message);
        return Ok(new { status = "success", message = result.Message });
    }

    // ==================== Standard Params ====================

    [HttpPost("{id:guid}/versions/{versionId:guid}/items/{itemId:guid}/standard-params")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> AddParam(Guid id, Guid versionId, Guid itemId, [FromBody] MaintenanceStandardParamDto dto)
    {
        var result = await _mediator.Send(new AddStandardParamCommand(
            id, versionId, itemId, dto.ParamName, dto.NominalValue, dto.ThresholdOperator,
            dto.ThresholdValue, dto.CheckMethod, dto.Unit, GetCurrentUserId()));

        if (!result.Success)
            return NotFoundOrBadRequest(result.ErrorCode, result.Message);
        return Ok(new
        {
            status = "success",
            data = new
            {
                // Key "id" verbatim (old: param.Id); "itemId" verbatim (old: literal itemId).
                Id = result.ParamId,
                result.ParamName,
                result.NominalValue,
                result.ThresholdOperator,
                result.ThresholdValue,
                result.CheckMethod,
                result.Unit,
                ItemId = result.ItemId
            }
        });
    }

    [HttpPut("{id:guid}/versions/{versionId:guid}/items/{itemId:guid}/standard-params/{paramId:guid}")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> UpdateParam(Guid id, Guid versionId, Guid itemId, Guid paramId, [FromBody] MaintenanceStandardParamDto dto)
    {
        var result = await _mediator.Send(new UpdateStandardParamCommand(
            id, versionId, itemId, paramId, dto.ParamName, dto.NominalValue, dto.ThresholdOperator,
            dto.ThresholdValue, dto.CheckMethod, dto.Unit, GetCurrentUserId()));

        if (!result.Success)
            return NotFoundOrBadRequest(result.ErrorCode, result.Message);
        return Ok(new { status = "success", message = result.Message });
    }

    [HttpDelete("{id:guid}/versions/{versionId:guid}/items/{itemId:guid}/standard-params/{paramId:guid}")]
    [Authorize(Policy = "maintenance.templates")]
    public async Task<IActionResult> DeleteParam(Guid id, Guid versionId, Guid itemId, Guid paramId)
    {
        var result = await _mediator.Send(new DeleteStandardParamCommand(id, versionId, itemId, paramId, GetCurrentUserId()));

        if (!result.Success)
            return NotFoundOrBadRequest(result.ErrorCode, result.Message);
        return Ok(new { status = "success", message = result.Message });
    }

    /// <summary>
    /// Verbatim error mapping: NOT_FOUND → 404 (message "Not found." cho template-level ẩn);
    /// VERSION_NOT_FOUND → 404 "Version not found."; còn lại → 400 + error_code snake_case.
    /// (ITEM_NOT_FOUND/PARAM_NOT_FOUND cũng 404 với đúng message gốc — handler đặt message sẵn.)
    /// </summary>
    private IActionResult NotFoundOrBadRequest(string? errorCode, string message)
    {
        if (errorCode == "NOT_FOUND")
            return NotFound(new { status = "error", message = "Not found." });
        if (errorCode == "VERSION_NOT_FOUND")
            return NotFound(new { status = "error", message = "Version not found." });
        if (errorCode == "ITEM_NOT_FOUND")
            return NotFound(new { status = "error", message = "Item not found." });
        if (errorCode == "PARAM_NOT_FOUND")
            return NotFound(new { status = "error", message = "Param not found." });
        return BadRequest(new { status = "error", message, error_code = errorCode });
    }
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
