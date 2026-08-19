using System.Security.Claims;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Web.Controllers;

[ApiController]
[Route("api/v1/component-units")]
[Authorize]
public class ComponentUnitsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IComponentAllocationService _allocationService;
    private readonly ICompanyScopeService _companyScope;

    public ComponentUnitsController(AppDbContext context, IComponentAllocationService allocationService, ICompanyScopeService companyScope)
    {
        _context = context;
        _allocationService = allocationService;
        _companyScope = companyScope;
    }

    private Task<Guid?> GetUserCompanyIdAsync() => _companyScope.GetCurrentUserCompanyIdAsync();

    /// <summary>Manually change a unit's status (e.g. mark Damaged/Disposed) with audit logging.</summary>
    [HttpPatch("{unitId:guid}")]
    [Authorize(Policy = "components.edit")]
    public async Task<IActionResult> UpdateStatus(Guid unitId, [FromBody] UpdateUnitStatusRequest r)
    {
        var result = await _allocationService.SetUnitStatusAsync(unitId, r.Status, r.Note, GetCurrentUserId());
        if (!result.Success) return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        return Ok(new { status = "success", message = result.Message });
    }

    /// <summary>
    /// Soft-deletes a serial unit that has NEVER been checked out. Units with allocation history
    /// must be disposed instead (their ActionLog audit trail must stay intact).
    /// </summary>
    [HttpDelete("{unitId:guid}")]
    [Authorize(Policy = "components.delete")]
    public async Task<IActionResult> Delete(Guid unitId)
    {
        // Logic (soft-delete, allocation-history guard, Qty decrement, ActionLog, company-scoping)
        // lives in IComponentAllocationService.DeleteUnitAsync so every future caller is protected too.
        var result = await _allocationService.DeleteUnitAsync(unitId, GetCurrentUserId());
        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                "NOT_FOUND" => NotFound(new { status = "error", message = result.Message }),
                _ => BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode })
            };
        }
        return Ok(new { status = "success", message = result.Message });
    }

    /// <summary>History of a single serial unit — trace every asset this unit passed through.</summary>
    [HttpGet("{unitId:guid}/action-logs")]
    [Authorize(Policy = "components.view")]
    public async Task<IActionResult> GetActionLogs(Guid unitId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        // Company scoping: a serial unit's history is only visible to users of its component's company.
        var userCompanyId = await GetUserCompanyIdAsync();
        var visible = await _context.ComponentUnits.AsNoTracking()
            .AnyAsync(u => u.Id == unitId && (userCompanyId == null || u.Component.CompanyId == null || u.Component.CompanyId == userCompanyId.Value));
        if (!visible) return NotFound(new { status = "error", message = "ComponentUnit not found." });

        var query = _context.ActionLogs.Include(l => l.Creator).AsNoTracking()
            .Where(l => l.ItemType == ItemType.ComponentUnit && l.ItemId == unitId)
            .OrderByDescending(l => l.ActionDate);

        var total = await query.CountAsync();
        var logs = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(l => new {
                l.Id, ItemType = l.ItemType.ToString(), l.ItemId,
                ActionType = l.ActionType.ToString(), ActionTypeValue = (int)l.ActionType,
                TargetType = l.TargetType.HasValue ? l.TargetType.Value.ToString() : null,
                l.TargetId,
                CreatorName = l.Creator != null
                    ? (l.Creator.FirstName + " " + l.Creator.LastName).Trim() != "" ? (l.Creator.FirstName + " " + l.Creator.LastName).Trim() : l.Creator.Username
                    : null,
                l.Note, l.LogMeta, l.ActionDate, l.LocationName, l.TargetSystemInfoName
            }).ToListAsync();

        var targetIds = logs.Where(x => x.TargetId.HasValue).Select(x => x.TargetId!.Value).Distinct().ToList();
        var assetNames = targetIds.Count > 0
            ? await _context.Assets.Where(a => targetIds.Contains(a.Id))
                .Select(a => new { a.Id, Name = a.AssetTag + " - " + a.Name })
                .ToDictionaryAsync(a => a.Id, a => a.Name)
            : new Dictionary<Guid, string>();

        var enriched = logs.Select(log => new {
            log.Id, log.ItemType, log.ItemId, log.ActionType, log.ActionTypeValue, log.TargetType, log.TargetId,
            TargetName = log.TargetId.HasValue ? assetNames.GetValueOrDefault(log.TargetId.Value) : null,
            log.CreatorName, log.Note, log.LogMeta, log.ActionDate, log.LocationName, log.TargetSystemInfoName
        }).ToList();

        return Ok(new { status = "success", data = enriched, total });
    }

    private Guid GetCurrentUserId()
    {
        // JIT provisioning stamps the local DB user id as "local_user_id" (Keycloak sub ≠ local id).
        if (Guid.TryParse(User.FindFirstValue("local_user_id"), out var local)) return local;
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}

public record UpdateUnitStatusRequest(ComponentUnitStatus Status, string? Note = null);
