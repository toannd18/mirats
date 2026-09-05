using aspire_react.Server.Application.MaintenanceCampaigns;
using aspire_react.Server.Application.MaintenanceCampaigns.Commands;
using aspire_react.Server.Application.MaintenanceCampaigns.Queries;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// MC-3 — Maintenance CAMPAIGN: create (auto device snapshot), checklist results (upsert), complete
/// (Status → Completed + SystemInfo.NextMaintenanceDueDate), campaign-level ActionLogs.
/// <para>
/// [Giai đoạn 3 — Rất nặng] MediatR migration: TOÀN BỘ 6 endpoint là thin IMediator.Send mapping
/// (2 queries + 4 commands). Guards verbatim trong handlers (BUG-A FOR UPDATE race-safe Create;
/// BUG-D retry-merge upsert 23505→409; [A3] MaintenanceChecklistRules dùng trực tiếp từ
/// Application/Maintenance — nguồn sự thật duy nhất, không đổi). Manual typed-log path giữ nguyên
/// (TargetSystemInfoId/Name + Create sở hữu transaction riêng — không ILoggableCommand).
/// Response keys ALIAS TƯỜNG MINH mọi nơi data.id cần "id" (bài học Templates/AssetMaintenances).
/// 409 RESULT_CONCURRENT_WRITE (BUG-D exhausted retries) — status mapping có precedent từ trước
/// migration (UsersController KEYCLOAK_*_EXISTS → Conflict).
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/maintenance/campaigns")]
[Authorize]
public class MaintenanceCampaignsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public MaintenanceCampaignsController(
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

    // ==================== List / Detail (maintenance.view) ====================

    [HttpGet]
    [Authorize(Policy = "maintenance.view")]
    public async Task<IActionResult> GetAll([FromQuery] Guid? systemInfoId)
    {
        var result = await _mediator.Send(new ListMaintenanceCampaignsQuery(systemInfoId));
        return Ok(new { status = "success", data = result.Items });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "maintenance.view")]
    public async Task<IActionResult> Get(Guid id)
    {
        var result = await _mediator.Send(new GetMaintenanceCampaignByIdQuery(id));
        if (!result.Success || result.Detail == null)
            return NotFound(new { status = "error", message = "Not found." });

        // DTO property names serialize đúng keys gốc (id, systemInfoName, templateId [MC-6],
        // executors/snapshots/results nested shapes) — đã đối chiếu key-by-key.
        return Ok(new { status = "success", data = result.Detail });
    }

    // ==================== Create (maintenance.campaigns) ====================

    [HttpPost]
    [Authorize(Policy = "maintenance.campaigns")]
    public async Task<IActionResult> Create([FromBody] CreateCampaignRequest dto)
    {
        var result = await _mediator.Send(new CreateCampaignCommand(
            dto.SystemInfoId, dto.TemplateId, dto.StartDate, dto.EndDate, dto.BatchNumber,
            dto.ReviewerId, dto.ExecutorIds, GetCurrentUserId()));

        if (!result.Success)
            return CreateError(result.ErrorCode!);

        // Keys verbatim: "id" (old campaign.Id — FE navigate res.data.data.id), "versionNumber"
        // (old computed VersionNumber = version.VersionNumber), "executorCount" (old literal).
        return Ok(new
        {
            status = "success",
            data = new
            {
                Id = result.CampaignId,
                result.SystemInfoId,
                result.TemplateVersionId,
                result.VersionNumber,
                result.StartDate,
                result.EndDate,
                result.BatchNumber,
                result.CompanyId,
                result.ReviewerId,
                result.Status,
                result.SnapshotCount,
                result.ExecutorCount
            }
        });
    }

    private IActionResult CreateError(string code) => code switch
    {
        "SYSTEM_NOT_FOUND" or "NOT_FOUND" => NotFound(new { status = "error", message = CreateMessage(code) }),
        _ => BadRequest(new { status = "error", message = CreateMessage(code), error_code = code })
    };

    private static string CreateMessage(string code) => code switch
    {
        "SYSTEM_INFO_REQUIRED" => "Hệ thống (SystemInfoId) là bắt buộc.",
        "SYSTEM_NOT_FOUND" => "Hệ thống không tồn tại hoặc ngoài phạm vi công ty của bạn.",
        "NOT_FOUND" => "Template not found.",
        "END_BEFORE_START" => "Ngày kết thúc không được trước ngày bắt đầu.",
        "TEMPLATE_SYSTEM_MISMATCH" => "Template không thuộc hệ thống đã chọn.",
        "NO_TEMPLATE" => "Hệ thống chưa có template bảo dưỡng.",
        "AMBIGUOUS_TEMPLATE" => "Hệ thống có nhiều template — cần chỉ định templateId.",
        "NO_CURRENT_VERSION" => "Template chưa có version hiện hành đã publish — hãy publish trước.",
        "INVALID_REVIEWER" => "Người duyệt không tồn tại.",
        "INVALID_EXECUTOR" => "Có người thực hiện không tồn tại trong hệ thống.",
        "EXECUTOR_COMPANY_MISMATCH" => "Người thực hiện phải thuộc cùng công ty với hệ thống.",
        "CAMPAIGN_ALREADY_IN_PROGRESS" => "Hệ thống đang có một đợt bảo dưỡng chưa hoàn thành — hãy hoàn thành hoặc chờ nó kết thúc.",
        _ => code
    };

    // ==================== Results (maintenance.campaigns, chỉ khi InProgress) ====================

    /// <summary>Upsert 1 kết quả. [MC-9] mỗi tiêu chuẩn có 1 dòng riêng; NULL = hạng mục không có tiêu chuẩn.</summary>
    [HttpPost("{id:guid}/results")]
    [Authorize(Policy = "maintenance.campaigns")]
    public async Task<IActionResult> UpsertResult(Guid id, [FromBody] UpsertCampaignResultDto dto)
    {
        var result = await _mediator.Send(new UpsertCampaignResultCommand(
            id, dto.DeviceSnapshotId, dto.ChecklistItemId, dto.MeasuredValue, dto.IsPass, dto.Notes,
            dto.StandardParamId, GetCurrentUserId()));

        if (!result.Success)
        {
            if (result.IsConflict)
                return Conflict(new { status = "error", message = "Kết quả vừa được ghi bởi request khác — thử lại.", error_code = result.ErrorCode });
            if (result.ErrorCode == "NOT_FOUND")
                return NotFound(new { status = "error", message = "Not found." });
            return BadRequest(new { status = "error", message = UpsertMessage(result.ErrorCode!), error_code = result.ErrorCode });
        }

        // Keys verbatim (old: existing.Id/...): "id" aliased explicitly.
        var r = result.Record!;
        return Ok(new
        {
            status = "success",
            data = new
            {
                Id = r.Id,
                r.DeviceSnapshotId,
                r.ChecklistItemId,
                r.StandardParamId,
                r.MeasuredValue,
                r.IsPass,
                r.Notes
            }
        });
    }

    private static string UpsertMessage(string code) => code switch
    {
        "CAMPAIGN_COMPLETED" => "Đợt bảo dưỡng đã hoàn thành — không thể sửa kết quả.",
        "RESULT_TARGET_REQUIRED" => "Cần chỉ định deviceSnapshotId và checklistItemId.",
        "INVALID_DEVICE_SNAPSHOT" => "DeviceSnapshot không thuộc đợt bảo dưỡng này.",
        "INVALID_CHECKLIST_ITEM" => "Hạng mục không thuộc checklist của version đã pin.",
        "INVALID_ITEM_POSITION" => "Hạng mục này chỉ áp dụng cho các vị trí đã khai báo trong template — thiết bị này không thuộc phạm vi.",
        "STANDARD_PARAM_NOT_APPLICABLE" => "Hạng mục này không có tiêu chuẩn kỹ thuật — không cần StandardParamId.",
        "STANDARD_PARAM_REQUIRED" => "Hạng mục này có tiêu chuẩn kỹ thuật — cần chỉ định StandardParamId.",
        "INVALID_STANDARD_PARAM" => "Tiêu chuẩn không thuộc hạng mục đã chọn.",
        _ => code
    };

    [HttpDelete("{id:guid}/results")]
    [Authorize(Policy = "maintenance.campaigns")]
    public async Task<IActionResult> DeleteResult(Guid id, [FromBody] DeleteCampaignResultDto dto)
    {
        var result = await _mediator.Send(new DeleteCampaignResultCommand(
            id, dto.DeviceSnapshotId, dto.ChecklistItemId, dto.StandardParamId, GetCurrentUserId()));

        if (!result.Success)
        {
            if (result.ErrorCode == "NOT_FOUND")
                return NotFound(new { status = "error", message = "Not found." });
            if (result.ErrorCode == "RESULT_NOT_FOUND")
                return NotFound(new { status = "error", message = "Kết quả không tồn tại." });
            return BadRequest(new { status = "error", message = "Đợt bảo dưỡng đã hoàn thành — không thể sửa kết quả.", error_code = result.ErrorCode });
        }

        return Ok(new { status = "success", message = "Result deleted." });
    }

    // ==================== Complete (maintenance.campaigns) ====================

    [HttpPost("{id:guid}/complete")]
    [Authorize(Policy = "maintenance.campaigns")]
    public async Task<IActionResult> Complete(Guid id)
    {
        var result = await _mediator.Send(new CompleteCampaignCommand(id, GetCurrentUserId()));

        if (!result.Success)
        {
            if (result.ErrorCode == "NOT_FOUND")
                return NotFound(new { status = "error", message = "Not found." });
            return BadRequest(new
            {
                status = "error",
                message = result.ErrorCode == "CAMPAIGN_RESULTS_INCOMPLETE"
                    ? result.ErrorMessage!
                    : "Đợt bảo dưỡng đã hoàn thành.",
                error_code = result.ErrorCode
            });
        }

        // Keys verbatim: "id" aliased; status string; nextMaintenanceDueDate computed.
        return Ok(new
        {
            status = "success",
            data = new
            {
                Id = result.CampaignId,
                result.Status,
                result.EndDate,
                result.NextMaintenanceDueDate,
                result.ReviewerId,
                result.ResultsCount
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
