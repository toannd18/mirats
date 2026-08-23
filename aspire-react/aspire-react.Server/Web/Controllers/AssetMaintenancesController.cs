using System.Text.Encodings.Web;
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

[ApiController]
[Route("api/v1")]
public class AssetMaintenancesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public AssetMaintenancesController(AppDbContext context, ICurrentUserService currentUserService, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    private Guid GetCurrentUserId() => _currentUserService.GetLocalUserId();

    /// <summary>
    /// Returns the current user's company scope: null = Superuser/no restriction (sees all).
    /// Regular users get their CompanyId — used to enforce company-scoped visibility.
    /// </summary>
    private Task<Guid?> GetUserCompanyIdAsync() => _companyScope.GetCurrentUserCompanyIdAsync();

    // ==================== LIST (asset-scoped — used by the Asset detail card) ====================

    [HttpGet("assets/{assetId:guid}/maintenances")]
    [Authorize(Policy = "assets.view")]
    public async Task<IActionResult> GetMaintenances(Guid assetId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var userCompanyId = await GetUserCompanyIdAsync();
        var asset = await _context.Assets.AsNoTracking()
            .Select(a => new { a.Id, a.CompanyId })
            .FirstOrDefaultAsync(a => a.Id == assetId);
        if (asset == null)
            return NotFound(new { status = "error", message = "Asset not found." });
        // Regular users may only view maintenances of assets in their own company (floater assets
        // with no company are visible to everyone).
        if (userCompanyId.HasValue && asset.CompanyId.HasValue && asset.CompanyId.Value != userCompanyId.Value)
            return Forbid();

        var query = _context.AssetMaintenances.AsNoTracking()
            .Include(m => m.Supplier)
            .Include(m => m.Asset)
            .Include(m => m.InspectedBy)
            .Include(m => m.Assignees).ThenInclude(a => a.User)
            .Where(m => m.AssetId == assetId && m.DeletedAt == null);

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(m => m.StartDate).ThenByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(m => new
            {
                m.Id,
                Type = m.Type.ToString(),
                m.Title,
                m.Notes,
                m.StartDate,
                m.CompletionDate,
                m.Cost,
                m.IsWarranty,
                m.CompanyId,
                Supplier = m.Supplier == null ? null : new { m.Supplier.Id, m.Supplier.Name },
                Asset = new { m.Asset.Id, m.Asset.AssetTag, m.Asset.Name },
                m.SnapshotSystemInfoId, m.SnapshotSystemInfoName,
                m.SnapshotSystemPositionId, m.SnapshotSystemPositionName,
                m.SnapshotLocationId, m.SnapshotLocationName,
                m.SnapshotAssignedUserId, m.SnapshotAssignedUserName,
                m.SnapshotDepartmentId, m.SnapshotDepartmentName,
                m.InspectedById, m.InspectedAt,
                InspectedByName = m.InspectedBy != null
                    ? (m.InspectedBy.FirstName + " " + m.InspectedBy.LastName).Trim() != "" ? (m.InspectedBy.FirstName + " " + m.InspectedBy.LastName).Trim() : m.InspectedBy.Username
                    : null,
                Assignees = m.Assignees.OrderBy(a => a.AssignedAt).Select(a => new
                {
                    a.UserId,
                    Name = (a.User.FirstName + " " + a.User.LastName).Trim() != "" ? (a.User.FirstName + " " + a.User.LastName).Trim() : a.User.Username,
                    a.AssignedAt
                }),
                m.CreatedAt, m.UpdatedAt
            }).ToListAsync();

        return Ok(new { status = "success", data = items, pagination = new { page, pageSize, totalItems = total, totalPages = (int)Math.Ceiling((double)total / pageSize), hasNextPage = page * pageSize < total, hasPreviousPage = page > 1 } });
    }

    // ==================== LIST (aggregated — used by /maintenances page; company-scoped) ====================

    [HttpGet("maintenances")]
    [Authorize(Policy = "assets.view")]
    public async Task<IActionResult> GetAllMaintenances([FromQuery] Guid? assetId, [FromQuery] Guid? systemInfoId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var userCompanyId = await GetUserCompanyIdAsync();

        var query = _context.AssetMaintenances.AsNoTracking()
            .Include(m => m.Supplier)
            .Include(m => m.Asset)
            .Include(m => m.Asset.Company)
            .Include(m => m.InspectedBy)
            .Include(m => m.Assignees).ThenInclude(a => a.User)
            .Where(m => m.DeletedAt == null);
        if (assetId.HasValue)
            query = query.Where(m => m.AssetId == assetId.Value);
        // System-scoped filter (SystemDetailPage Maintenance tab): the maintenance record belongs to
        // the system the asset was in AT CREATION TIME (immutable SnapshotSystemInfoId). This keeps
        // the historical view correct even if the asset has been re-parented since.
        if (systemInfoId.HasValue)
            query = query.Where(m => m.SnapshotSystemInfoId == systemInfoId.Value);
        // Regular users only see records of their own company (floater records with no company
        // are visible to everyone); Superuser sees all.
        if (userCompanyId.HasValue)
            query = query.Where(m => m.CompanyId == userCompanyId.Value || m.CompanyId == Guid.Empty);

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(m => m.StartDate).ThenByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(m => new
            {
                m.Id,
                Type = m.Type.ToString(),
                m.Title,
                m.Notes,
                m.StartDate,
                m.CompletionDate,
                m.Cost,
                m.IsWarranty,
                m.IsClosed,
                m.CompanyId,
                Supplier = m.Supplier == null ? null : new { m.Supplier.Id, m.Supplier.Name },
                Asset = new { m.Asset.Id, m.Asset.AssetTag, m.Asset.Name, CompanyName = m.Asset.Company != null ? m.Asset.Company.Name : (string?)null },
                m.SnapshotSystemInfoId, m.SnapshotSystemInfoName,
                m.SnapshotSystemPositionId, m.SnapshotSystemPositionName,
                m.SnapshotLocationId, m.SnapshotLocationName,
                m.SnapshotAssignedUserId, m.SnapshotAssignedUserName,
                m.SnapshotDepartmentId, m.SnapshotDepartmentName,
                m.InspectedById, m.InspectedAt,
                InspectedByName = m.InspectedBy != null
                    ? (m.InspectedBy.FirstName + " " + m.InspectedBy.LastName).Trim() != "" ? (m.InspectedBy.FirstName + " " + m.InspectedBy.LastName).Trim() : m.InspectedBy.Username
                    : null,
                Assignees = m.Assignees.OrderBy(a => a.AssignedAt).Select(a => new
                {
                    a.UserId,
                    Name = (a.User.FirstName + " " + a.User.LastName).Trim() != "" ? (a.User.FirstName + " " + a.User.LastName).Trim() : a.User.Username,
                    a.AssignedAt
                }),
                m.CreatedAt, m.UpdatedAt
            }).ToListAsync();

        return Ok(new { status = "success", data = items, pagination = new { page, pageSize, totalItems = total, totalPages = (int)Math.Ceiling((double)total / pageSize), hasNextPage = page * pageSize < total, hasPreviousPage = page > 1 } });
    }

    // ==================== DETAIL ====================

    [HttpGet("maintenances/{id:guid}")]
    [Authorize(Policy = "assets.view")]
    public async Task<IActionResult> GetMaintenance(Guid id)
    {
        var m = await _context.AssetMaintenances.AsNoTracking()
            .Include(x => x.Supplier)
            .Include(x => x.Asset.SystemPosition).ThenInclude(sp => sp.SystemInfo)
            .Include(x => x.Asset.Location)
            .Include(x => x.Asset.CurrentAssignment)
            .Include(x => x.InspectedBy)
            .Include(x => x.Assignees).ThenInclude(a => a.User)
            .FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null);
        if (m == null) return NotFound(new { status = "error", message = "Maintenance not found." });

        // 403 (not 404): the record exists but the regular user's company cannot view it.
        var userCompanyId = await GetUserCompanyIdAsync();
        if (userCompanyId.HasValue && m.CompanyId != userCompanyId.Value && m.CompanyId != Guid.Empty)
            return Forbid();

        // LIVE context of the asset RIGHT NOW (computed on the fly, never stored) so viewers can
        // compare "how it was during maintenance" (Snapshot*) vs "how it is today".
        var cur = await BuildSnapshotAsync(m.Asset, CancellationToken.None);

        return Ok(new { status = "success", data = new
        {
            m.Id,
            Type = m.Type.ToString(),
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
            m.SnapshotSystemInfoId, m.SnapshotSystemInfoName,
            m.SnapshotSystemPositionId, m.SnapshotSystemPositionName,
            m.SnapshotLocationId, m.SnapshotLocationName,
            m.SnapshotAssignedUserId, m.SnapshotAssignedUserName,
            m.SnapshotDepartmentId, m.SnapshotDepartmentName,
            m.InspectedById, m.InspectedAt,
            InspectedByName = m.InspectedBy != null
                ? (m.InspectedBy.FirstName + " " + m.InspectedBy.LastName).Trim() != "" ? (m.InspectedBy.FirstName + " " + m.InspectedBy.LastName).Trim() : m.InspectedBy.Username
                : null,
            Assignees = m.Assignees.OrderBy(a => a.AssignedAt).Select(a => new
            {
                a.UserId,
                Name = (a.User.FirstName + " " + a.User.LastName).Trim() != "" ? (a.User.FirstName + " " + a.User.LastName).Trim() : a.User.Username,
                a.AssignedAt
            }),
            m.CreatedAt, m.UpdatedAt,
            currentContext = new
            {
                systemInfoId = cur.SysInfoId,
                systemInfoName = cur.SysInfoName,
                systemPositionId = cur.PosId,
                systemPositionName = cur.PosName,
                locationId = cur.LocId,
                locationName = cur.LocName,
                assignedUserId = cur.UserId,
                assignedUserName = cur.UserName,
                departmentId = cur.DeptId,
                departmentName = cur.DeptName
            }
        }});
    }

    [HttpPost("assets/{assetId:guid}/maintenances")]
    [Authorize(Policy = "assets.edit")]
    public Task<IActionResult> Create(Guid assetId, [FromBody] CreateAssetMaintenanceRequest r)
        => CreateCoreAsync(assetId, r);

    // ==================== CREATE (aggregated — picks the asset from the body) ====================

    [HttpPost("maintenances")]
    [Authorize(Policy = "assets.edit")]
    public Task<IActionResult> CreateForAsset([FromBody] CreateAssetMaintenanceForAssetRequest r)
        => CreateCoreAsync(r.AssetId, new CreateAssetMaintenanceRequest(r.Type, r.Title, r.Notes, r.SupplierId,
            r.StartDate, r.CompletionDate, r.Cost, r.IsWarranty, r.AssigneeUserIds));

    /// <summary>
    /// Shared create logic: validates input, enforces company scope (a regular user may only create
    /// maintenance for an asset in their own company), snapshots the asset context once, and writes
    /// the record + ActionLog. CompanyId is set by the server (= Asset.CompanyId ?? Guid.Empty) and
    /// is locked afterwards.
    /// </summary>
    private async Task<IActionResult> CreateCoreAsync(Guid assetId, CreateAssetMaintenanceRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Title))
            return BadRequest(new { status = "error", message = "Tiêu đề (Title) là bắt buộc.", error_code = "TITLE_REQUIRED" });
        if (r.CompletionDate.HasValue && r.CompletionDate.Value < r.StartDate)
            return BadRequest(new { status = "error", message = "Ngày hoàn thành không được trước ngày bắt đầu.", error_code = "COMPLETION_BEFORE_START" });
        if (r.Cost.HasValue && r.Cost.Value < 0)
            return BadRequest(new { status = "error", message = "Chi phí không được âm.", error_code = "INVALID_COST" });
        if (r.SupplierId.HasValue && !await _context.Suppliers.AnyAsync(s => s.Id == r.SupplierId.Value))
            return BadRequest(new { status = "error", message = "Nhà cung cấp không hợp lệ.", error_code = "INVALID_SUPPLIER" });

        var asset = await _context.Assets
            .Include(a => a.SystemPosition).ThenInclude(sp => sp.SystemInfo)
            .Include(a => a.Location)
            .Include(a => a.CurrentAssignment)
            .FirstOrDefaultAsync(a => a.Id == assetId);
        if (asset == null) return NotFound(new { status = "error", message = "Asset not found." });

        // Company scope (defense in depth — do not trust the client-side asset filter).
        var userCompanyId = await GetUserCompanyIdAsync();
        if (userCompanyId.HasValue && asset.CompanyId.HasValue && asset.CompanyId.Value != userCompanyId.Value)
            return Forbid();

        // Assignees (optional on create): max 5 + same-company rule, validated against the server-set
        // maintenance company (= Asset.CompanyId ?? Guid.Empty, so floater records allow any user).
        var assigneeError = await ValidateAssigneesAsync(r.AssigneeUserIds, asset.CompanyId ?? Guid.Empty);
        if (assigneeError != null) return assigneeError;

        var snap = await BuildSnapshotAsync(asset, CancellationToken.None);

        var m = new AssetMaintenance
        {
            AssetId = assetId,
            Type = r.Type,
            Title = r.Title.Trim(),
            Notes = r.Notes,
            SupplierId = r.SupplierId,
            CompanyId = asset.CompanyId ?? Guid.Empty,
            StartDate = DateTime.SpecifyKind(r.StartDate, DateTimeKind.Unspecified),
            CompletionDate = r.CompletionDate.HasValue ? DateTime.SpecifyKind(r.CompletionDate.Value, DateTimeKind.Unspecified) : null,
            Cost = r.Cost,
            IsWarranty = r.IsWarranty,
            SnapshotSystemInfoId = snap.SysInfoId,
            SnapshotSystemInfoName = snap.SysInfoName,
            SnapshotSystemPositionId = snap.PosId,
            SnapshotSystemPositionName = snap.PosName,
            SnapshotLocationId = snap.LocId,
            SnapshotLocationName = snap.LocName,
            SnapshotAssignedUserId = snap.UserId,
            SnapshotAssignedUserName = snap.UserName,
            SnapshotDepartmentId = snap.DeptId,
            SnapshotDepartmentName = snap.DeptName,
            CreatedById = GetCurrentUserId(),
            CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
            UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
        };
        _context.AssetMaintenances.Add(m);
        if (r.AssigneeUserIds != null)
        {
            foreach (var uid in r.AssigneeUserIds.Distinct())
            {
                _context.AssetMaintenanceAssignees.Add(new AssetMaintenanceAssignee
                {
                    MaintenanceId = m.Id,
                    UserId = uid,
                    AssignedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                });
            }
        }
        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.AssetMaintenance,
            ItemId = m.Id,
            ActionType = ActionType.Create,
            CreatedBy = GetCurrentUserId(),
            CompanyId = m.CompanyId,
            Note = $"Tạo bảo trì \"{m.Title}\""
        });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Đã tạo bảo trì.", data = new { m.Id } });
    }

    // ==================== UPDATE (whitelist; snapshot fields are locked) ====================

    [HttpPut("maintenances/{id:guid}")]
    [Authorize(Policy = "assets.edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAssetMaintenanceRequest r)
    {
        var m = await _context.AssetMaintenances.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null);
        if (m == null) return NotFound(new { status = "error", message = "Maintenance not found." });

        // [SEC-FIX S1, 2026-08-23] Company scoping — same rule as Close/Inspect/Create/Detail in this
        // controller: a regular user may only edit records of their own company (floater records with
        // CompanyId == Guid.Empty are manageable by everyone); Superuser bypasses. Previously Update
        // had NO scope check at all (verified empirically: a user from company A could edit a record
        // of company B by id, including replacing its assignees). Out-of-scope → 404 (hide existence).
        var userCompanyId = await GetUserCompanyIdAsync();
        if (userCompanyId.HasValue && m.CompanyId != userCompanyId.Value && m.CompanyId != Guid.Empty)
            return NotFound(new { status = "error", message = "Maintenance not found." });

        // Absolute lock: a closed record is immutable (audit-trail protection) — reject ALL fields.
        if (m.IsClosed)
            return BadRequest(new { status = "error", message = "Bản ghi đã đóng, không thể chỉnh sửa.", error_code = "MAINTENANCE_CLOSED" });

        // Locked fields — AssetId (from route), snapshot fields (never sent in DTO), StartDate:
        // reject when a DIFFERENT value is supplied so the user understands history is immutable.
        if (r.StartDate.HasValue && r.StartDate.Value != m.StartDate)
            return BadRequest(new { status = "error", message = "Không thể thay đổi (field đã khóa): startDate.", error_code = "FIELD_LOCKED" });

        if (r.CompletionDate.HasValue && r.CompletionDate.Value < m.StartDate)
            return BadRequest(new { status = "error", message = "Ngày hoàn thành không được trước ngày bắt đầu.", error_code = "COMPLETION_BEFORE_START" });
        if (r.Cost.HasValue && r.Cost.Value < 0)
            return BadRequest(new { status = "error", message = "Chi phí không được âm.", error_code = "INVALID_COST" });
        if (r.SupplierId.HasValue && !await _context.Suppliers.AnyAsync(s => s.Id == r.SupplierId.Value))
            return BadRequest(new { status = "error", message = "Nhà cung cấp không hợp lệ.", error_code = "INVALID_SUPPLIER" });

        // Whitelist: Title, Notes, Type, SupplierId, CompletionDate, Cost, IsWarranty
        if (!string.IsNullOrWhiteSpace(r.Title)) m.Title = r.Title.Trim();
        m.Notes = r.Notes ?? m.Notes;
        if (r.Type.HasValue) m.Type = r.Type.Value;
        m.SupplierId = r.SupplierId;
        m.CompletionDate = r.CompletionDate.HasValue ? DateTime.SpecifyKind(r.CompletionDate.Value, DateTimeKind.Unspecified) : null;
        m.Cost = r.Cost;
        if (r.IsWarranty.HasValue) m.IsWarranty = r.IsWarranty.Value;
        m.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        // Assignee list (replace-all, max 5, company-scoped). It may STILL be edited after the record
        // has been inspected — only the CLOSE step freezes it (the IsClosed guard above rejects any
        // edit on a closed record, which includes the assignee list).
        if (r.AssigneeUserIds != null)
        {
            var assigneeError = await ValidateAssigneesAsync(r.AssigneeUserIds, m.CompanyId);
            if (assigneeError != null) return assigneeError;
            await ReplaceAssigneesAsync(id, r.AssigneeUserIds);
        }

        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.AssetMaintenance,
            ItemId = id,
            ActionType = ActionType.Update,
            CreatedBy = GetCurrentUserId(),
            CompanyId = m.CompanyId,
            Note = $"Cập nhật bảo trì \"{m.Title}\""
        });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Đã cập nhật bảo trì." });
    }

    // ==================== DELETE (Superuser only + ActionLog with deleted content) ====================

    [HttpDelete("maintenances/{id:guid}")]
    [Authorize(Policy = "assets.edit")]
    public async Task<IActionResult> Delete(Guid id)
    {
        // Only Superuser may delete maintenance records (history/audit data).
        if (!_companyScope.IsSuperUser())
            return Forbid();

        var m = await _context.AssetMaintenances.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null);
        if (m == null) return NotFound(new { status = "error", message = "Maintenance not found." });

        m.DeletedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        m.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        // Log the deletion with the FULL record content (incl. snapshot) so the audit trail
        // keeps enough to reconstruct what was removed.
        var meta = JsonSerializer.Serialize(new
        {
            title = m.Title,
            type = m.Type.ToString(),
            startDate = m.StartDate,
            completionDate = m.CompletionDate,
            cost = m.Cost,
            supplierId = m.SupplierId,
            snapshotSystemInfoId = m.SnapshotSystemInfoId,
            snapshotSystemInfoName = m.SnapshotSystemInfoName,
            snapshotSystemPositionId = m.SnapshotSystemPositionId,
            snapshotSystemPositionName = m.SnapshotSystemPositionName,
            snapshotLocationId = m.SnapshotLocationId,
            snapshotLocationName = m.SnapshotLocationName,
            snapshotAssignedUserId = m.SnapshotAssignedUserId,
            snapshotAssignedUserName = m.SnapshotAssignedUserName,
            snapshotDepartmentId = m.SnapshotDepartmentId,
            snapshotDepartmentName = m.SnapshotDepartmentName
        }, new JsonSerializerOptions
        {
            // Keep Vietnamese/diacritics readable inside the audit trail.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.AssetMaintenance,
            ItemId = id,
            ActionType = ActionType.Delete,
            CreatedBy = GetCurrentUserId(),
            CompanyId = m.CompanyId,
            Note = $"Xóa bảo trì \"{m.Title}\" (superuser)",
            LogMeta = meta
        });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Đã xóa bảo trì." });
    }

    // ==================== CLOSE / REOPEN (audit-trail lock) ====================
    // Closing is the natural completion step of the workflow: anyone who may edit the record
    // (same company or Superuser) may close it, but only once CompletionDate is set. After close
    // the record is frozen (PUT rejects ALL fields with MAINTENANCE_CLOSED). Reopening is a
    // Superuser-only action that "breaks" the audit protection — it keeps ClosedAt/ClosedById as
    // the history of the most recent close; every close/reopen is itself recorded in ActionLog.

    [HttpPost("maintenances/{id:guid}/close")]
    [Authorize(Policy = "assets.edit")]
    public async Task<IActionResult> Close(Guid id)
    {
        var m = await _context.AssetMaintenances.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null);
        if (m == null) return NotFound(new { status = "error", message = "Maintenance not found." });

        // Same edit rights as Update: a regular user may close records of their own company
        // (floater records are manageable by everyone).
        var userCompanyId = await GetUserCompanyIdAsync();
        if (userCompanyId.HasValue && m.CompanyId != userCompanyId.Value && m.CompanyId != Guid.Empty)
            return Forbid();

        if (m.IsClosed)
            return BadRequest(new { status = "error", message = "Bản ghi đã đóng.", error_code = "MAINTENANCE_ALREADY_CLOSED" });
        if (m.CompletionDate == null)
            return BadRequest(new { status = "error", message = "Cần nhập Ngày hoàn thành trước khi đóng bảo trì.", error_code = "MAINTENANCE_NOT_COMPLETED_YET" });
        // Inspection is an independent pre-close approval step — a completed-but-not-yet-inspected
        // record cannot be closed (workflow: Hoàn thành → Kiểm tra → Đóng).
        if (m.InspectedById == null)
            return BadRequest(new { status = "error", message = "Cần kiểm tra trước khi đóng bảo trì.", error_code = "MAINTENANCE_NOT_INSPECTED_YET" });

        m.IsClosed = true;
        m.ClosedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        m.ClosedById = GetCurrentUserId();
        m.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.AssetMaintenance,
            ItemId = id,
            ActionType = ActionType.Close,
            CreatedBy = GetCurrentUserId(),
            CompanyId = m.CompanyId,
            Note = $"Đóng bảo trì \"{m.Title}\""
        });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Đã đóng bảo trì.", data = new { m.Id, m.IsClosed, m.ClosedAt, m.ClosedById } });
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
        var m = await _context.AssetMaintenances.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null);
        if (m == null) return NotFound(new { status = "error", message = "Maintenance not found." });

        // Same edit rights as Close/Update: a regular user may inspect records of their own company
        // (floater records are manageable by everyone).
        var userCompanyId = await GetUserCompanyIdAsync();
        if (userCompanyId.HasValue && m.CompanyId != userCompanyId.Value && m.CompanyId != Guid.Empty)
            return Forbid();

        if (m.IsClosed)
            return BadRequest(new { status = "error", message = "Bản ghi đã đóng, không thể kiểm tra.", error_code = "MAINTENANCE_CLOSED" });
        if (m.CompletionDate == null)
            return BadRequest(new { status = "error", message = "Cần nhập Ngày hoàn thành trước khi kiểm tra bảo trì.", error_code = "MAINTENANCE_NOT_COMPLETED_YET" });

        m.InspectedById = GetCurrentUserId();
        m.InspectedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        m.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.AssetMaintenance,
            ItemId = id,
            ActionType = ActionType.Inspect,
            CreatedBy = GetCurrentUserId(),
            CompanyId = m.CompanyId,
            Note = $"Kiểm tra bảo trì \"{m.Title}\""
        });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Đã đánh dấu đã kiểm tra.", data = new { m.Id, m.InspectedById, m.InspectedAt } });
    }

    [HttpPost("maintenances/{id:guid}/reopen")]
    [Authorize]
    public async Task<IActionResult> Reopen(Guid id)
    {
        // Reopen breaks the audit lock — Superuser only (consistent with delete rights).
        if (!_companyScope.IsSuperUser())
            return Forbid();

        var m = await _context.AssetMaintenances.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null);
        if (m == null) return NotFound(new { status = "error", message = "Maintenance not found." });
        if (!m.IsClosed)
            return BadRequest(new { status = "error", message = "Bản ghi chưa đóng.", error_code = "MAINTENANCE_NOT_CLOSED" });

        m.IsClosed = false;
        // Keep ClosedAt/ClosedById — they record the most recent close (each cycle is audited in ActionLog).
        m.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.AssetMaintenance,
            ItemId = id,
            ActionType = ActionType.Reopen,
            CreatedBy = GetCurrentUserId(),
            CompanyId = m.CompanyId,
            Note = $"Mở lại bảo trì \"{m.Title}\" (superuser)"
        });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Đã mở lại bảo trì.", data = new { m.Id, m.IsClosed, m.ClosedAt, m.ClosedById } });
    }

    // ==================== Assignee helpers ====================

    /// <summary>
    /// Validates the assignee list: distinct, max 5, all users must exist, and (for regular users
    /// operating on a company-scoped record) every assignee must belong to the SAME company as the
    /// maintenance record. Superuser and floater records (CompanyId == Guid.Empty) are unrestricted.
    /// Returns null when valid, otherwise a 400 result to return to the caller.
    /// </summary>
    private async Task<IActionResult?> ValidateAssigneesAsync(Guid[]? assigneeUserIds, Guid maintenanceCompanyId)
    {
        if (assigneeUserIds == null || assigneeUserIds.Length == 0) return null;

        var distinct = assigneeUserIds.Distinct().ToArray();
        if (distinct.Length > 5)
            return BadRequest(new { status = "error", message = "Tối đa 5 người phụ trách cho một bản ghi bảo trì.", error_code = "MAX_5_ASSIGNEES" });

        var users = await _context.Users.AsNoTracking()
            .Where(u => distinct.Contains(u.Id))
            .Select(u => new { u.Id, u.CompanyId })
            .ToListAsync();
        if (users.Count != distinct.Length)
            return BadRequest(new { status = "error", message = "Có người phụ trách không tồn tại trong hệ thống.", error_code = "INVALID_ASSIGNEE" });

        // Company isolation (same principle as the user picker in other modules): a regular user may
        // only assign users of the SAME company as the record. Superuser (userCompanyId == null) and
        // floater records (Guid.Empty, manageable by everyone) skip the check.
        var userCompanyId = await GetUserCompanyIdAsync();
        if (userCompanyId.HasValue && maintenanceCompanyId != Guid.Empty
            && users.Any(u => u.CompanyId != maintenanceCompanyId))
            return BadRequest(new { status = "error", message = "Người phụ trách phải thuộc cùng công ty với bản ghi bảo trì.", error_code = "ASSIGNEE_COMPANY_MISMATCH" });

        return null;
    }

    /// <summary>Replaces the whole assignee list (remove-all + insert distinct users).</summary>
    private async Task ReplaceAssigneesAsync(Guid maintenanceId, Guid[]? assigneeUserIds)
    {
        var existing = await _context.AssetMaintenanceAssignees
            .Where(a => a.MaintenanceId == maintenanceId)
            .ToListAsync();
        _context.AssetMaintenanceAssignees.RemoveRange(existing);

        if (assigneeUserIds != null)
        {
            foreach (var uid in assigneeUserIds.Distinct())
            {
                _context.AssetMaintenanceAssignees.Add(new AssetMaintenanceAssignee
                {
                    MaintenanceId = maintenanceId,
                    UserId = uid,
                    AssignedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                });
            }
        }
    }

    // ==================== Snapshot builder ====================

    /// <summary>
    /// Captures the asset's context at THIS moment: SystemPosition + its parent SystemInfo
    /// (both levels — SystemPosition is a child of SystemInfo), Location, assigned User, and
    /// Department. Mirrors the ActionLog write-time snapshot convention (id + display name).
    /// </summary>
    private async Task<(Guid? SysInfoId, string? SysInfoName, Guid? PosId, string? PosName,
        Guid? LocId, string? LocName, Guid? UserId, string? UserName, Guid? DeptId, string? DeptName)>
        BuildSnapshotAsync(Asset asset, CancellationToken ct)
    {
        Guid? sysInfoId = null;
        string? sysInfoName = null;
        Guid? posId = null;
        string? posName = null;
        if (asset.SystemPosition != null)
        {
            posId = asset.SystemPosition.Id;
            posName = asset.SystemPosition.Name;
            sysInfoId = asset.SystemPosition.SystemInfoId;
            sysInfoName = asset.SystemPosition.SystemInfo?.Name;
        }

        Guid? locId = asset.LocationId;
        string? locName = asset.Location?.Name;

        Guid? userId = null;
        string? userName = null;
        Guid? deptId = null;
        string? deptName = null;
        if (asset.CurrentAssignment != null)
        {
            var asgn = asset.CurrentAssignment;
            if (asgn.TargetType == AssignmentTargetType.User)
            {
                userId = asgn.TargetId;
                var user = await _context.Users.AsNoTracking()
                    .Where(u => u.Id == userId.Value)
                    .Select(u => new
                    {
                        DisplayName = (u.FirstName + " " + u.LastName).Trim() != ""
                            ? (u.FirstName + " " + u.LastName).Trim()
                            : u.Username,
                        u.DepartmentId,
                        DeptName = u.Department != null ? u.Department.Name : (string?)null
                    })
                    .FirstOrDefaultAsync(ct);
                if (user != null)
                {
                    userName = user.DisplayName;
                    deptId = user.DepartmentId;
                    deptName = user.DeptName;
                }
            }
            else if (asgn.TargetType == AssignmentTargetType.Department)
            {
                deptId = asgn.TargetId;
                deptName = await _context.Departments.AsNoTracking()
                    .Where(d => d.Id == deptId.Value)
                    .Select(d => d.Name)
                    .FirstOrDefaultAsync(ct);
            }
        }

        return (sysInfoId, sysInfoName, posId, posName, locId, locName, userId, userName, deptId, deptName);
    }
}

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
