using System.Security.Claims;
using aspire_react.Server.Application.Locations.Commands;
using aspire_react.Server.Application.Locations.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// [Giai đoạn 2] Locations extracted from AdminController as a STANDALONE controller
/// (playbook §6 decision — same as Categories). Route strings unchanged: /api/v1/locations...
/// Company-scoping on GetAll/GetById/Update/Delete (Create is the known BUG-G gap — parity
/// preserved, see docs/BACKLOG.md). NO output-cache on this section (pre-migration had none) —
/// commands implement ILoggableCommand only, deliberately NOT ICacheInvalidatingCommand.
/// GetById is NEW (approved): scoped-404 matching GetAll/Update/Delete (not Create).
/// </summary>
[ApiController, Route("api/v1/locations"), Authorize]
public class LocationsController : ControllerBase
{
    private readonly IMediator _mediator;
    public LocationsController(IMediator mediator)
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

    [HttpGet, Authorize(Policy = "locations.view")]
    public async Task<IActionResult> GetLocations([FromQuery] Guid? companyId)
    {
        var list = await _mediator.Send(new ListLocationsQuery(companyId));
        return Ok(new { status = "success", data = list });
    }

    [HttpGet("{id:guid}"), Authorize(Policy = "locations.view")]
    public async Task<IActionResult> GetLocation(Guid id)
    {
        var l = await _mediator.Send(new GetLocationByIdQuery(id));
        if (l is null)
            return NotFound(new { status = "error", message = "Not found." });
        return Ok(new { status = "success", data = l });
    }

    [HttpPost, Authorize(Policy = "locations.create")]
    public async Task<IActionResult> CreateLocation([FromBody] CreateLocationRequest r)
    {
        // TODO SECURITY BUG-G: request carries CompanyId with NO scoping/validation — verbatim
        // pre-migration behavior (see docs/BACKLOG.md, SECURITY/HIGH).
        var result = await _mediator.Send(new CreateLocationCommand(
            r.Name, r.ParentId, r.CompanyId, r.ManagerId, r.Address, r.City, r.State, r.Country, r.Zip,
            GetCurrentUserId()));

        if (!result.Success)
            return MapFailure(result);

        return Ok(new { status = "success", data = new { Id = result.LocationId } });
    }

    [HttpPut("{id:guid}"), Authorize(Policy = "locations.edit")]
    public async Task<IActionResult> UpdateLocation(Guid id, [FromBody] UpdateLocationRequest r)
    {
        var result = await _mediator.Send(new UpdateLocationCommand(
            id, r.Name, r.ParentId, r.CompanyId, r.ManagerId, r.Address, r.City, r.State, r.Country, r.Zip,
            GetCurrentUserId()));

        if (!result.Success)
            return MapFailure(result);

        return Ok(new { status = "success", message = "Updated." });
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = "locations.delete")]
    public async Task<IActionResult> DeleteLocation(Guid id)
    {
        var result = await _mediator.Send(new DeleteLocationCommand(id, GetCurrentUserId()));

        if (!result.Success)
            return MapFailure(result);

        return Ok(new { status = "success", message = "Deleted." });
    }

    /// <summary>
    /// Maps a LocationResult failure to the EXACT same HTTP bodies as the pre-migration
    /// controller: NOT_FOUND → 404 without error_code; null ErrorCode (tree has-children guard)
    /// → 400 without error_code (old body had none); LOCATION_IN_USE → 400 with error_code.
    /// </summary>
    private IActionResult MapFailure(LocationResult result)
    {
        if (result.ErrorCode == "NOT_FOUND")
            return NotFound(new { status = "error", message = result.Message });

        object body = result.ErrorCode is null
            ? new { status = "error", message = result.Message }
            : new { status = "error", message = result.Message, error_code = result.ErrorCode };
        return BadRequest(body);
    }
}

/// <summary>Request DTO for POST /api/v1/locations (was previously mass-bound to the Location entity).</summary>
public record CreateLocationRequest(
    string Name, Guid? ParentId, Guid? CompanyId, Guid? ManagerId,
    string? Address, string? City, string? State, string? Country, string? Zip);

/// <summary>Patch-style Update DTO for Location (Task M2 semantics preserved) — nullable fields.</summary>
public record UpdateLocationRequest(
    string? Name, Guid? ParentId, Guid? CompanyId, Guid? ManagerId,
    string? Address, string? City, string? State, string? Country, string? Zip);
