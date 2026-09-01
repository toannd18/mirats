using System.Security.Claims;
using aspire_react.Server.Application.Common;
using aspire_react.Server.Application.Manufacturers.Commands;
using aspire_react.Server.Application.Manufacturers.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// [Giai đoạn 2] Manufacturers extracted from AdminController as a STANDALONE controller
/// (playbook §6 decision). Route strings unchanged: /api/v1/manufacturers...
/// Reference data — NO company-scoping (no CompanyId, by design — NOT a bug).
/// Create/Update/Delete dispatch Commands implementing BOTH ILoggableCommand (log thin →
/// enrichment 2a) and ICacheInvalidatingCommand (evict ref:manufacturers). GetById is NEW
/// (playbook §6.5 pattern).
/// </summary>
[ApiController, Route("api/v1/manufacturers"), Authorize]
public class ManufacturersController : ControllerBase
{
    private readonly IMediator _mediator;
    public ManufacturersController(IMediator mediator)
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

    [HttpGet, Authorize(Policy = "manufacturers.view")]
    [OutputCache(PolicyName = "RefData", Tags = [CacheTags.Manufacturers])] // Task P: reference-data, no CompanyId, same for all authorized users
    public async Task<IActionResult> GetManufacturers()
    {
        var list = await _mediator.Send(new ListManufacturersQuery());
        return Ok(new { status = "success", data = list });
    }

    [HttpGet("{id:guid}"), Authorize(Policy = "manufacturers.view")]
    public async Task<IActionResult> GetManufacturer(Guid id)
    {
        var m = await _mediator.Send(new GetManufacturerByIdQuery(id));
        if (m is null)
            return NotFound(new { status = "error", message = "Not found." });
        return Ok(new { status = "success", data = m });
    }

    [HttpPost, Authorize(Policy = "manufacturers.create")]
    public async Task<IActionResult> CreateManufacturer([FromBody] CreateManufacturerRequest r)
    {
        var result = await _mediator.Send(new CreateManufacturerCommand(
            r.Code, r.Name, r.Url, r.SupportUrl, r.SupportEmail, GetCurrentUserId()));

        if (!result.Success)
            return MapFailure(result);

        return Ok(new { status = "success", data = new { Id = result.ManufacturerId, Code = result.Code, Name = result.Name } });
    }

    [HttpPut("{id:guid}"), Authorize(Policy = "manufacturers.edit")]
    public async Task<IActionResult> UpdateManufacturer(Guid id, [FromBody] UpdateManufacturerRequest r)
    {
        var result = await _mediator.Send(new UpdateManufacturerCommand(
            id, r.Code, r.Name, r.Url, r.SupportUrl, r.SupportEmail, GetCurrentUserId()));

        if (!result.Success)
            return MapFailure(result);

        return Ok(new { status = "success", message = "Updated." });
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = "manufacturers.delete")]
    public async Task<IActionResult> DeleteManufacturer(Guid id)
    {
        var result = await _mediator.Send(new DeleteManufacturerCommand(id, GetCurrentUserId()));

        if (!result.Success)
            return MapFailure(result);

        return Ok(new { status = "success", message = "Deleted." });
    }

    /// <summary>
    /// Maps a ManufacturerResult failure to the EXACT same HTTP bodies as the pre-migration
    /// controller: NOT_FOUND → 404 without error_code; null ErrorCode (Code length / dup rules)
    /// → 400 without error_code (old bodies had none); MANUFACTURER_IN_USE → 400 with error_code.
    /// </summary>
    private IActionResult MapFailure(ManufacturerResult result)
    {
        if (result.ErrorCode == "NOT_FOUND")
            return NotFound(new { status = "error", message = result.Message });

        object body = result.ErrorCode is null
            ? new { status = "error", message = result.Message }
            : new { status = "error", message = result.Message, error_code = result.ErrorCode };
        return BadRequest(body);
    }
}

/// <summary>Request DTO for POST /api/v1/manufacturers (was previously mass-bound to the Manufacturer entity).</summary>
public record CreateManufacturerRequest(string Code, string Name, string? Url, string? SupportUrl, string? SupportEmail);

/// <summary>Patch-style Update DTO for Manufacturer — nullable fields (Task M2 semantics preserved).</summary>
public record UpdateManufacturerRequest(string? Code, string? Name, string? Url, string? SupportUrl, string? SupportEmail);
