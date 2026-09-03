using System.Security.Claims;
using aspire_react.Server.Application.ComponentUnits.Commands;
using aspire_react.Server.Application.ComponentUnits.Queries;
using aspire_react.Server.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// [Giai đoạn 3] ComponentUnits migrated to MediatR — serial-unit management (3 endpoints).
/// Commands DELEGATE to IComponentAllocationService (interface moved to Domain/Interfaces) —
/// the allocation/lock/ActionLog logic stays in the Infrastructure service untouched, so the
/// FOR UPDATE concurrency semantics are preserved exactly.
/// Error mapping verbatim: UpdateStatus failure → 400 with error_code; Delete failure →
/// NOT_FOUND → 404, other → 400 with error_code.
/// </summary>
[ApiController]
[Route("api/v1/component-units")]
[Authorize]
public class ComponentUnitsController : ControllerBase
{
    private readonly IMediator _mediator;
    public ComponentUnitsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    private Guid GetCurrentUserId()
    {
        // [SEC-FIX CLAIM-CLEANUP, 2026-08-23] ONLY "local_user_id" (JIT-stamped). Keycloak
        // sub/preferred_username are never a user identity source (bug-class 1). Absent → Guid.Empty.
        if (Guid.TryParse(User.FindFirstValue("local_user_id"), out var local)) return local;
        return Guid.Empty;
    }

    /// <summary>Manually change a unit's status (e.g. mark Damaged/Disposed) with audit logging.</summary>
    [HttpPatch("{unitId:guid}")]
    [Authorize(Policy = "components.edit")]
    public async Task<IActionResult> UpdateStatus(Guid unitId, [FromBody] UpdateUnitStatusRequest r)
    {
        var result = await _mediator.Send(new UpdateComponentUnitStatusCommand(unitId, r.Status, r.Note, GetCurrentUserId()));
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
        var result = await _mediator.Send(new DeleteComponentUnitCommand(unitId, GetCurrentUserId()));
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
        var result = await _mediator.Send(new GetComponentUnitLogsQuery(unitId, page, pageSize));
        if (result == null) return NotFound(new { status = "error", message = "ComponentUnit not found." });

        return Ok(new { status = "success", data = result.Items, total = result.Total });
    }
}

/// <summary>Request DTO — verbatim from the pre-migration record.</summary>
public record UpdateUnitStatusRequest(ComponentUnitStatus Status, string? Note = null);
