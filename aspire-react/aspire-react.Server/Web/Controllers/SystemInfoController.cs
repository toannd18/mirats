using System.Security.Claims;
using aspire_react.Server.Application.SystemInfos.Commands;
using aspire_react.Server.Application.SystemInfos.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// [Giai đoạn 3] SystemInfo + Positions migrated to MediatR — company-scoped maintenance
/// infrastructure (FMCS read + Task L2 write scoping + SEC-FIX P1 patch-aware DTOs + MC-7a/
/// BUG-C delete guards + FIELD_LOCKED). 8 endpoints, routes unchanged: /api/v1/system-infos...
/// CUD = ILoggableCommand with CompanyId = resource company (NOT null — company-scoped resource).
/// No output-cache → no ICacheInvalidatingCommand.
/// </summary>
[ApiController, Route("api/v1/system-infos"), Authorize]
public class SystemInfoController : ControllerBase
{
    private readonly IMediator _mediator;
    public SystemInfoController(IMediator mediator)
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

    [HttpGet, Authorize(Policy = "systems.view")]
    public async Task<IActionResult> GetAll()
    {
        var list = await _mediator.Send(new ListSystemInfosQuery());
        return Ok(new { status = "success", data = list });
    }

    [HttpGet("{id:guid}"), Authorize(Policy = "systems.view")]
    public async Task<IActionResult> Get(Guid id)
    {
        var s = await _mediator.Send(new GetSystemInfoByIdQuery(id));
        if (s == null) return NotFound(new { status = "error", message = "Not found." });
        return Ok(new { status = "success", data = s });
    }

    [HttpPost, Authorize(Policy = "systems.create")]
    public async Task<IActionResult> Create([FromBody] SystemInfoDto dto)
    {
        var result = await _mediator.Send(new CreateSystemInfoCommand(dto.Code, dto.Name, dto.Description, dto.CompanyId, GetCurrentUserId()));
        if (!result.Success) return MapFailure(result);
        return Ok(new { status = "success", data = new { result.Id, result.Code, result.Name } });
    }

    [HttpPut("{id:guid}"), Authorize(Policy = "systems.edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SystemInfoDto dto)
    {
        var result = await _mediator.Send(new UpdateSystemInfoCommand(id, dto.Code, dto.Name, dto.Description, dto.CompanyId, GetCurrentUserId()));
        if (!result.Success) return MapFailure(result);
        return Ok(new { status = "success", message = "Updated." });
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = "systems.delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await _mediator.Send(new DeleteSystemInfoCommand(id, GetCurrentUserId()));
        if (!result.Success) return MapFailure(result);
        return Ok(new { status = "success", message = "Deleted." });
    }

    // === Positions ===

    [HttpPost("{systemInfoId:guid}/positions"), Authorize(Policy = "systems.create")]
    public async Task<IActionResult> AddPosition(Guid systemInfoId, [FromBody] SystemPositionDto dto)
    {
        var result = await _mediator.Send(new AddSystemPositionCommand(systemInfoId, dto.Code, dto.Name, dto.Description, GetCurrentUserId()));
        if (!result.Success) return MapFailure(result);
        return Ok(new { status = "success", data = new { result.Id, result.Code, result.Name } });
    }

    [HttpPut("{systemInfoId:guid}/positions/{posId:guid}"), Authorize(Policy = "systems.edit")]
    public async Task<IActionResult> UpdatePosition(Guid systemInfoId, Guid posId, [FromBody] SystemPositionDto dto)
    {
        var result = await _mediator.Send(new UpdateSystemPositionCommand(systemInfoId, posId, dto.Code, dto.Name, dto.Description, GetCurrentUserId()));
        if (!result.Success) return MapFailure(result);
        return Ok(new { status = "success", message = "Position updated." });
    }

    [HttpDelete("{systemInfoId:guid}/positions/{posId:guid}"), Authorize(Policy = "systems.delete")]
    public async Task<IActionResult> DeletePosition(Guid systemInfoId, Guid posId)
    {
        var result = await _mediator.Send(new DeleteSystemPositionCommand(systemInfoId, posId, GetCurrentUserId()));
        if (!result.Success) return MapFailure(result);
        return Ok(new { status = "success", message = "Position deleted." });
    }

    /// <summary>
    /// Maps a SystemInfoResult failure to the EXACT same HTTP bodies as the pre-migration
    /// controller: NOT_FOUND → 404 (message carries Not found./System not found./Position not
    /// found.); null ErrorCode (regex/empty/dup-code) → 400 WITHOUT error_code; COMPANY_MISMATCH/
    /// FIELD_LOCKED/POSITION_IN_USE_BY_CHECKLIST/SYSTEM_IN_USE_BY_CAMPAIGN → 400 WITH error_code.
    /// </summary>
    private IActionResult MapFailure(SystemInfoResult result)
    {
        if (result.ErrorCode == "NOT_FOUND")
            return NotFound(new { status = "error", message = result.Message });

        object body = result.ErrorCode is null
            ? new { status = "error", message = result.Message }
            : new { status = "error", message = result.Message, error_code = result.ErrorCode };
        return BadRequest(body);
    }
}

// [SEC-FIX P1] Patch-aware DTOs: Code/Name are nullable so a PARTIAL update payload does not
// wipe absent fields (Task F/M1 pattern). Create validates presence explicitly; Update assigns
// only what was actually sent.
public record SystemInfoDto(string? Code, string? Name, string? Description, Guid? CompanyId = null);
public record SystemPositionDto(string? Code, string? Name, string? Description);
