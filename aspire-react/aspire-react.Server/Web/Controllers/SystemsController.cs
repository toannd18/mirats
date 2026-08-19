using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// System-scoped read endpoints for the SystemDetailPage.
/// System model: SystemInfo (parent) → SystemPosition (child). Assets link to a SystemPosition
/// (Asset.SystemPositionId); Accessories check out to a SystemPosition (AccessoryCheckout with
/// CheckoutType = SystemPosition). Both endpoints aggregate across ALL child positions so that
/// viewing a SystemInfo parent shows everything installed / checked-out under it.
/// </summary>
[ApiController, Route("api/v1/systems"), Authorize]
public class SystemsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public SystemsController(AppDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    /// <summary>
    /// Same semantics as /action-logs/by-system: superuser or no configured companies → unrestricted;
    /// otherwise the system must be company-less or belong to one of the user's companies.
    /// Callers return 404 when this is false.
    ///
    /// DELIBERATE CONVENTION — 404 (NOT 403) for out-of-scope systems: the existence of a system
    /// (its code + name) is company-sensitive, so it is hidden entirely from users of other companies.
    /// This matches SystemInfoController.Get and ActionLogsController.GetBySystem. Single maintenance
    /// records (AssetMaintenancesController) intentionally use 403 instead — a lone record is far less
    /// sensitive and is reached via an already-scoped list. Do NOT unify the two status codes.
    /// </summary>
    private async Task<bool> IsSystemVisibleAsync(Guid systemId, CancellationToken ct = default)
    {
        var userCompanyIds = await _companyScope.GetUserCompanyIdsAsync();
        return await _context.SystemInfos.AsNoTracking().AnyAsync(s =>
            s.Id == systemId &&
            (_companyScope.IsSuperUser() || userCompanyIds.Count == 0 ||
             s.CompanyId == null || userCompanyIds.Contains(s.CompanyId.Value)), ct);
    }
    // ==================== ASSETS ====================

    /// <summary>
    /// Assets currently installed in the system. An Asset links to a SystemPosition (child); the
    /// parent SystemInfo is implied — so this aggregates across every child position of the system.
    /// Pass systemPositionId to narrow to a single position (used by the position quick-filter).
    /// </summary>
    [HttpGet("{id:guid}/assets")]
    [Authorize(Policy = "assets.view")]
    public async Task<IActionResult> GetAssets(Guid id, [FromQuery] Guid? systemPositionId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (!await IsSystemVisibleAsync(id))
            return NotFound(new { status = "error", message = "Hệ thống không tồn tại hoặc ngoài phạm vi công ty của bạn." });

        var query = _context.Assets.AsNoTracking()
            .Include(a => a.Location)
            .Include(a => a.Company)
            .Include(a => a.SystemPosition)
            .Include(a => a.CurrentAssignment)
            .Where(a => a.SystemPosition != null && a.SystemPosition.SystemInfoId == id);

        if (systemPositionId.HasValue)
            query = query.Where(a => a.SystemPositionId == systemPositionId.Value);

        // Defense in depth (same as Asset Maintenance list): a regular user with a configured company
        // may only see assets of their own company; company-less floaters are visible to everyone.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue)
            query = query.Where(a => a.CompanyId == null || a.CompanyId == userCompanyId.Value);

        var total = await query.CountAsync();
        var assets = await query.OrderBy(a => a.AssetTag).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new
            {
                a.Id, a.AssetTag, a.Name, a.Serial,
                Status = a.Status.ToString(),
                SystemPosition = a.SystemPosition == null
                    ? null
                    : new { a.SystemPosition.Id, a.SystemPosition.Code, a.SystemPosition.Name },
                Location = a.Location == null ? null : new { a.Location.Id, a.Location.Name },
                Company = a.Company == null ? null : new { a.Company.Id, a.Company.Name },
                AssignedTo = a.CurrentAssignment == null
                    ? null
                    : new { type = a.CurrentAssignment.TargetType.ToString(), targetId = a.CurrentAssignment.TargetId }
            })
            .ToListAsync();

        // Batch-resolve assigned-to names + the assigned user's department (mirrors AssetsController).
        var atAssets = assets.Where(a => a.AssignedTo != null).Select(a => a.AssignedTo!).ToList();
        var uDict = new Dictionary<Guid, string>();
        var dDict = new Dictionary<Guid, string>();
        var pDict = new Dictionary<Guid, string>();
        var deptOfUser = new Dictionary<Guid, Guid?>();
        if (atAssets.Any())
        {
            var uids = atAssets.Where(x => x.type == "User").Select(x => x.targetId).Distinct().ToList();
            var dids = atAssets.Where(x => x.type == "Department").Select(x => x.targetId).Distinct().ToList();
            var pids = atAssets.Where(x => x.type == "SystemPosition").Select(x => x.targetId).Distinct().ToList();
            if (uids.Any())
            {
                var users = await _context.Users.AsNoTracking()
                    .Where(u => uids.Contains(u.Id))
                    .Select(u => new
                    {
                        u.Id,
                        Display = (u.FirstName + " " + u.LastName).Trim() != "" ? (u.FirstName + " " + u.LastName).Trim() : u.Username,
                        u.DepartmentId
                    })
                    .ToListAsync();
                foreach (var u in users)
                {
                    uDict[u.Id] = u.Display;
                    deptOfUser[u.Id] = u.DepartmentId;
                }
            }
            if (dids.Any())
                dDict = await _context.Departments.Where(d => dids.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name);
            if (pids.Any())
                pDict = await _context.SystemPositions.Where(sp => pids.Contains(sp.Id)).ToDictionaryAsync(sp => sp.Id, sp => sp.Name);
        }
        var deptIds = deptOfUser.Values.Where(v => v.HasValue).Select(v => v!.Value).Distinct().ToList();
        var deptNameDict = new Dictionary<Guid, string>();
        if (deptIds.Any())
            deptNameDict = await _context.Departments.Where(d => deptIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name);
        var enriched = assets.Select(a =>
        {
            string? an = null;
            Guid? assignedDeptId = null;
            if (a.AssignedTo != null)
            {
                an = a.AssignedTo.type switch
                {
                    "User" => uDict.GetValueOrDefault(a.AssignedTo.targetId),
                    "Department" => dDict.GetValueOrDefault(a.AssignedTo.targetId),
                    "SystemPosition" => pDict.GetValueOrDefault(a.AssignedTo.targetId),
                    _ => null
                };
                if (a.AssignedTo.type == "User")
                    assignedDeptId = deptOfUser.GetValueOrDefault(a.AssignedTo.targetId);
            }
            return new
            {
                a.Id, a.AssetTag, a.Name, a.Serial, a.Status, a.SystemPosition, a.Location, a.Company,
                AssignedTo = a.AssignedTo == null ? null : new { a.AssignedTo.type, a.AssignedTo.targetId, name = an },
                Department = assignedDeptId.HasValue && deptNameDict.TryGetValue(assignedDeptId.Value, out var dn)
                    ? new { id = assignedDeptId.Value, name = dn }
                    : null
            };
        }).ToList();

        return Ok(new
        {
            status = "success",
            data = enriched,
            pagination = new
            {
                page, pageSize, totalItems = total,
                totalPages = (int)Math.Ceiling((double)total / pageSize),
                hasNextPage = page * pageSize < total,
                hasPreviousPage = page > 1
            }
        });
    }
    // ==================== ACCESSORIES ====================

    /// <summary>
    /// Accessories checked out to the system — aggregate across every child SystemPosition
    /// (AccessoryCheckout.CheckoutType = SystemPosition, TargetId = SystemPosition.Id).
    /// Pass systemPositionId to narrow to a single position.
    /// </summary>
    [HttpGet("{id:guid}/accessories")]
    [Authorize(Policy = "accessories.view")]
    public async Task<IActionResult> GetAccessories(Guid id, [FromQuery] Guid? systemPositionId = null)
    {
        if (!await IsSystemVisibleAsync(id))
            return NotFound(new { status = "error", message = "Hệ thống không tồn tại hoặc ngoài phạm vi công ty của bạn." });

        var positionIds = await _context.SystemPositions.AsNoTracking()
            .Where(sp => sp.SystemInfoId == id)
            .Select(sp => sp.Id)
            .ToListAsync();

        if (positionIds.Count == 0)
            return Ok(new { status = "success", data = Array.Empty<object>() });

        var query = _context.AccessoryCheckouts.AsNoTracking()
            .Include(ch => ch.Accessory)
            .Include(ch => ch.CreatedByUser)
            .Where(ch => ch.CheckoutType == AccessoryCheckoutType.SystemPosition && positionIds.Contains(ch.TargetId));

        if (systemPositionId.HasValue)
            query = query.Where(ch => ch.TargetId == systemPositionId.Value);

        // Defense in depth: same company rule as the accessory checkout command (an accessory is
        // scoped to its company; company-less accessories are visible to everyone).
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue)
            query = query.Where(ch => ch.Accessory.CompanyId == null || ch.Accessory.CompanyId == userCompanyId.Value);

        var items = await query.OrderByDescending(ch => ch.CheckedOutAt)
            .Select(ch => new
            {
                ch.Id,
                ch.AccessoryId,
                AccessoryName = ch.Accessory.Name,
                AccessoryItemNo = ch.Accessory.ItemNo,
                ch.AssignedQty,
                ch.ReturnedQty,
                RemainingCheckedOut = ch.AssignedQty - ch.ReturnedQty,
                SystemPositionId = ch.TargetId,
                ch.Note,
                ch.CheckedOutAt,
                CreatedByUserId = ch.CreatedByUserId,
                CreatedByUsername = ch.CreatedByUser != null ? ch.CreatedByUser.Username : null,
                CreatedByFirstName = ch.CreatedByUser != null ? ch.CreatedByUser.FirstName : null,
                CreatedByLastName = ch.CreatedByUser != null ? ch.CreatedByUser.LastName : null
            })
            .ToListAsync();

        var posIds = items.Select(i => i.SystemPositionId).Distinct().ToList();
        var posDict = new Dictionary<Guid, (string Code, string Name)>();
        if (posIds.Any())
        {
            posDict = await _context.SystemPositions.AsNoTracking()
                .Where(sp => posIds.Contains(sp.Id))
                .Select(sp => new { sp.Id, sp.Code, sp.Name })
                .ToDictionaryAsync(sp => sp.Id, sp => (sp.Code, sp.Name));
        }

        var enriched = items.Select(i => new
        {
            i.Id,
            i.AccessoryId,
            i.AccessoryName,
            i.AccessoryItemNo,
            i.AssignedQty,
            i.ReturnedQty,
            i.RemainingCheckedOut,
            SystemPosition = posDict.TryGetValue(i.SystemPositionId, out var p)
                ? new { id = i.SystemPositionId, code = p.Code, name = p.Name }
                : null,
            i.Note,
            i.CheckedOutAt,
            i.CreatedByUserId,
            CreatedByName = (i.CreatedByFirstName + " " + i.CreatedByLastName).Trim() != ""
                ? (i.CreatedByFirstName + " " + i.CreatedByLastName).Trim()
                : i.CreatedByUsername
        }).ToList();

        return Ok(new { status = "success", data = enriched });
    }
}
