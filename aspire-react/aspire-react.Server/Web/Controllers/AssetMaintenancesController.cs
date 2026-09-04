using aspire_react.Server.Application.AssetMaintenances.Commands;
using aspire_react.Server.Application.AssetMaintenances.Queries;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

[ApiController]
[Route("api/v1")]
/// <summary>
/// [Giai đoạn 3 — Rất nặng] AssetMaintenances MediatR migration — HOÀN TẤT (subtasks A–D).
/// Toàn bộ 9 endpoint là thin IMediator.Send mapping: reads (subtask A: asset-scoped list /
/// aggregated list / detail), Create 2-routes→1-command (B), Update+Delete (C),
/// Close/Inspect/Reopen (D). Guard order/scope bodies verbatim trong handlers.
/// Parity trap (a): reads + Close/Inspect out-of-scope → Forbid() 403, Update → 404 — giữ nguyên.
/// Fields _context/_companyScope/_actionLogService không còn dùng nhưng GIỮ ctor signature
/// (tests + DI construct với đủ 5 tham số; giữ là chi phí zero, xóa là churn tests).
/// </summary>
public class AssetMaintenancesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public AssetMaintenancesController(IMediator mediator, AppDbContext context, ICurrentUserService currentUserService, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _mediator = mediator;
        _context = context;
        _currentUserService = currentUserService;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    private Guid GetCurrentUserId() => _currentUserService.GetLocalUserId();

    // ==================== LIST (asset-scoped — used by the Asset detail card) ====================
    // [Subtask A] Thin MediatR mapping over ListAssetMaintenancesQuery (scope + shape verbatim
    // trong handler; out-of-scope → Forbid() 403, verbatim trap (a)).

    [HttpGet("assets/{assetId:guid}/maintenances")]
    [Authorize(Policy = "assets.view")]
    public async Task<IActionResult> GetMaintenances(Guid assetId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _mediator.Send(new ListAssetMaintenancesQuery(assetId, page, pageSize));
        if (result.ErrorCode == "NOT_FOUND")
            return NotFound(new { status = "error", message = "Asset not found." });
        if (result.ErrorCode == "FORBIDDEN")
            return Forbid();

        return Ok(new { status = "success", data = result.Items, pagination = new { page, pageSize, totalItems = result.Total, totalPages = (int)Math.Ceiling((double)result.Total / pageSize), hasNextPage = page * pageSize < result.Total, hasPreviousPage = page > 1 } });
    }

    // ==================== LIST (aggregated — used by /maintenances page; company-scoped) ====================
    // [Subtask A] Thin MediatR mapping over ListAllMaintenancesQuery (filters + scope verbatim).

    [HttpGet("maintenances")]
    [Authorize(Policy = "assets.view")]
    public async Task<IActionResult> GetAllMaintenances([FromQuery] Guid? assetId, [FromQuery] Guid? systemInfoId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _mediator.Send(new ListAllMaintenancesQuery(assetId, systemInfoId, page, pageSize));

        return Ok(new { status = "success", data = result.Items, pagination = new { page, pageSize, totalItems = result.Total, totalPages = (int)Math.Ceiling((double)result.Total / pageSize), hasNextPage = page * pageSize < result.Total, hasPreviousPage = page > 1 } });
    }

    // ==================== DETAIL ====================
    // [Subtask A] Thin MediatR mapping over GetMaintenanceByIdQuery (scope + currentContext
    // verbatim trong handler; out-of-scope → Forbid() 403, verbatim trap (a)).

    [HttpGet("maintenances/{id:guid}")]
    [Authorize(Policy = "assets.view")]
    public async Task<IActionResult> GetMaintenance(Guid id)
    {
        var result = await _mediator.Send(new GetMaintenanceByIdQuery(id));
        if (result.ErrorCode == "NOT_FOUND")
            return NotFound(new { status = "error", message = "Maintenance not found." });
        if (result.ErrorCode == "FORBIDDEN")
            return Forbid();

        var m = result.Detail!;
        return Ok(new
        {
            status = "success",
            data = new
            {
                m.Id,
                m.Type,
                m.Title,
                m.Notes,
                m.StartDate,
                m.CompletionDate,
                m.Cost,
                m.IsWarranty,
                m.IsClosed,
                m.ClosedAt,
                m.ClosedById,
                m.CompanyId,
                Supplier = m.Supplier == null ? null : new { m.Supplier.Id, m.Supplier.Name },
                Asset = new { m.Asset.Id, m.Asset.AssetTag, m.Asset.Name },
                m.SnapshotSystemInfoId,
                m.SnapshotSystemInfoName,
                m.SnapshotSystemPositionId,
                m.SnapshotSystemPositionName,
                m.SnapshotLocationId,
                m.SnapshotLocationName,
                m.SnapshotAssignedUserId,
                m.SnapshotAssignedUserName,
                m.SnapshotDepartmentId,
                m.SnapshotDepartmentName,
                m.InspectedById,
                m.InspectedAt,
                InspectedByName = m.InspectedByName,
                Assignees = m.Assignees.Select(a => new
                {
                    a.UserId,
                    a.Name,
                    a.AssignedAt
                }),
                m.CreatedAt,
                m.UpdatedAt,
                currentContext = new
                {
                    systemInfoId = m.CurrentContext.SystemInfoId,
                    systemInfoName = m.CurrentContext.SystemInfoName,
                    systemPositionId = m.CurrentContext.SystemPositionId,
                    systemPositionName = m.CurrentContext.SystemPositionName,
                    locationId = m.CurrentContext.LocationId,
                    locationName = m.CurrentContext.LocationName,
                    assignedUserId = m.CurrentContext.AssignedUserId,
                    assignedUserName = m.CurrentContext.AssignedUserName,
                    departmentId = m.CurrentContext.DepartmentId,
                    departmentName = m.CurrentContext.DepartmentName
                }
            }
        });
    }

    // ==================== CREATE (2 routes → 1 shared command, subtask B) ====================
    // Audit verdict: both routes funneled into the SAME CreateCoreAsync with zero divergence
    // (the aggregated route only repacks its DTO field-by-field) → ONE CreateMaintenanceCommand.
    // Thin MediatR mappings; validations/scope/snapshot/assignees verbatim in the handler.

    [HttpPost("assets/{assetId:guid}/maintenances")]
    [Authorize(Policy = "assets.edit")]
    public async Task<IActionResult> Create(Guid assetId, [FromBody] CreateAssetMaintenanceRequest r)
        => await CreateViaCommandAsync(assetId, r.Type, r.Title, r.Notes, r.SupplierId,
            r.StartDate, r.CompletionDate, r.Cost, r.IsWarranty, r.AssigneeUserIds);

    // ==================== CREATE (aggregated — picks the asset from the body) ====================

    [HttpPost("maintenances")]
    [Authorize(Policy = "assets.edit")]
    public async Task<IActionResult> CreateForAsset([FromBody] CreateAssetMaintenanceForAssetRequest r)
        => await CreateViaCommandAsync(r.AssetId, r.Type, r.Title, r.Notes, r.SupplierId,
            r.StartDate, r.CompletionDate, r.Cost, r.IsWarranty, r.AssigneeUserIds);

    /// <summary>
    /// Shared create mapping: sends the single CreateMaintenanceCommand and maps failures to the
    /// EXACT pre-migration bodies (NOT_FOUND → 404; FORBIDDEN → Forbid() 403; validation rules →
    /// 400 with error_code snake_case).
    /// </summary>
    private async Task<IActionResult> CreateViaCommandAsync(Guid assetId, AssetMaintenanceType type, string title,
        string? notes, Guid? supplierId, DateTime startDate, DateTime? completionDate, decimal? cost,
        bool isWarranty, Guid[]? assigneeUserIds)
    {
        var result = await _mediator.Send(new CreateMaintenanceCommand(
            assetId, type, title, notes, supplierId, startDate, completionDate, cost,
            isWarranty, assigneeUserIds, GetCurrentUserId()));

        if (!result.Success)
        {
            if (result.ErrorCode == "NOT_FOUND")
                return NotFound(new { status = "error", message = result.Message });
            if (result.ErrorCode == "FORBIDDEN")
                return Forbid();
            return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        }

        return Ok(new { status = "success", message = result.Message, data = new { Id = result.MaintenanceId } });
    }

    // ==================== UPDATE (whitelist; snapshot fields are locked) ====================
    // [Subtask C] Thin MediatR mapping over UpdateMaintenanceCommand. Guard order + whitelist +
    // FIELD_LOCKED(StartDate) + MAINTENANCE_CLOSED verbatim trong handler. Scope 404 (S1,
    // hide-existence — verbatim trap (a), KHÔNG đổi thành 403 như reads).

    [HttpPut("maintenances/{id:guid}")]
    [Authorize(Policy = "assets.edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAssetMaintenanceRequest r)
    {
        var result = await _mediator.Send(new UpdateMaintenanceCommand(
            id, r.Title, r.Notes, r.Type, r.SupplierId, r.CompletionDate, r.Cost,
            r.IsWarranty, r.AssigneeUserIds, r.StartDate, GetCurrentUserId()));

        if (!result.Success)
        {
            if (result.ErrorCode == "NOT_FOUND")
                return NotFound(new { status = "error", message = result.Message });
            return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        }

        return Ok(new { status = "success", message = result.Message });
    }

    // ==================== DELETE (Superuser only + ActionLog with deleted content) ====================
    // [Subtask C] Thin MediatR mapping over DeleteMaintenanceCommand (soft-delete + full-content
    // LogMeta với UnsafeRelaxedJsonEscaping verbatim trong handler; superuser-gate TRƯỚC lookup).

    [HttpDelete("maintenances/{id:guid}")]
    [Authorize(Policy = "assets.edit")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await _mediator.Send(new DeleteMaintenanceCommand(id, GetCurrentUserId()));

        if (!result.Success)
        {
            if (result.ErrorCode == "NOT_FOUND")
                return NotFound(new { status = "error", message = result.Message });
            if (result.ErrorCode == "FORBIDDEN")
                return Forbid();
            return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        }

        return Ok(new { status = "success", message = result.Message });
    }

    // ==================== CLOSE / REOPEN (audit-trail lock) ====================
    // [Subtask D] Thin MediatR mapping over Close/Inspect/ReopenMaintenanceCommand. Guard order +
    // freeze semantics verbatim trong handler. Scope 403 Forbid verbatim (trap (a): reads AND
    // Close/Inspect dùng 403, chỉ Update dùng 404 — KHÔNG thống nhất); Reopen superuser-gate TRƯỚC
    // lookup. Closing is the natural completion step of the workflow: anyone who may edit the record
    // (same company or Superuser) may close it, but only once CompletionDate is set. After close
    // the record is frozen (PUT rejects ALL fields with MAINTENANCE_CLOSED). Reopening is a
    // Superuser-only action that "breaks" the audit protection — it keeps ClosedAt/ClosedById as
    // the history of the most recent close; every close/reopen is itself recorded in ActionLog.

    [HttpPost("maintenances/{id:guid}/close")]
    [Authorize(Policy = "assets.edit")]
    public async Task<IActionResult> Close(Guid id)
    {
        var result = await _mediator.Send(new CloseMaintenanceCommand(id, GetCurrentUserId()));

        if (!result.Success)
        {
            if (result.ErrorCode == "NOT_FOUND")
                return NotFound(new { status = "error", message = result.Message });
            if (result.ErrorCode == "FORBIDDEN")
                return Forbid();
            return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        }

        // Key "id" verbatim (old: m.Id) — result.MaintenanceId would serialize "maintenanceId".
        return Ok(new { status = "success", message = result.Message, data = new { Id = result.MaintenanceId, result.IsClosed, result.ClosedAt, result.ClosedById } });
    }

    // ==================== INSPECT (independent pre-close approval step) ====================

    /// <summary>
    /// Marks the maintenance as inspected — the independent approval step BETWEEN "Hoàn thành"
    /// (CompletionDate) and "Đóng" (Close). Anyone who may edit the record (same company or
    /// Superuser) may inspect it; the step may be repeated (overwrites InspectedBy/InspectedAt,
    /// i.e. "inspect again") and does NOT lock anything — only Close freezes the record.
    /// </summary>
    [HttpPost("maintenances/{id:guid}/inspect")]
    [Authorize(Policy = "assets.edit")]
    public async Task<IActionResult> Inspect(Guid id)
    {
        var result = await _mediator.Send(new InspectMaintenanceCommand(id, GetCurrentUserId()));

        if (!result.Success)
        {
            if (result.ErrorCode == "NOT_FOUND")
                return NotFound(new { status = "error", message = result.Message });
            if (result.ErrorCode == "FORBIDDEN")
                return Forbid();
            return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        }

        // Key "id" verbatim (old: m.Id); InspectedById/InspectedAt verbatim (old: m.InspectedById/m.InspectedAt).
        return Ok(new { status = "success", message = result.Message, data = new { Id = result.MaintenanceId, result.InspectedById, result.InspectedAt } });
    }

    [HttpPost("maintenances/{id:guid}/reopen")]
    [Authorize]
    public async Task<IActionResult> Reopen(Guid id)
    {
        var result = await _mediator.Send(new ReopenMaintenanceCommand(id, GetCurrentUserId()));

        if (!result.Success)
        {
            if (result.ErrorCode == "NOT_FOUND")
                return NotFound(new { status = "error", message = result.Message });
            if (result.ErrorCode == "FORBIDDEN")
                return Forbid();
            return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        }

        // Key "id" verbatim (old: m.Id) — same parity fix as Close.
        return Ok(new { status = "success", message = result.Message, data = new { Id = result.MaintenanceId, result.IsClosed, result.ClosedAt, result.ClosedById } });
    }

    // ==================== Assignee helpers (moved to Application in subtask C) ====================
    // ValidateAssigneesAsync + ReplaceAssigneesAsync now live in
    // Application/AssetMaintenances/Commands/MaintenanceAssignees.cs (shared by Create/Update).
    // Close/Inspect/Reopen (subtask D) never touch assignees.
}

// ==================== Request records ====================

public record CreateAssetMaintenanceRequest(AssetMaintenanceType Type, string Title, string? Notes, Guid? SupplierId,
    DateTime StartDate, DateTime? CompletionDate, decimal? Cost, bool IsWarranty = false, Guid[]? AssigneeUserIds = null);

public record CreateAssetMaintenanceForAssetRequest(Guid AssetId, AssetMaintenanceType Type, string Title, string? Notes,
    Guid? SupplierId, DateTime StartDate, DateTime? CompletionDate, decimal? Cost, bool IsWarranty = false, Guid[]? AssigneeUserIds = null);

public record UpdateAssetMaintenanceRequest(string? Title = null, string? Notes = null, AssetMaintenanceType? Type = null,
    Guid? SupplierId = null, DateTime? CompletionDate = null, decimal? Cost = null, bool? IsWarranty = null,
    // Assignee list — replace-all; null = leave unchanged, [] = clear all. Max 5, company-scoped.
    Guid[]? AssigneeUserIds = null,
    // Locked-field detection — rejected if a DIFFERENT value is supplied.
    DateTime? StartDate = null);
