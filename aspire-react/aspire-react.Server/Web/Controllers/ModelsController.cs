using System.Security.Claims;
using aspire_react.Server.Application.Models.Commands;
using aspire_react.Server.Application.Models.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// [Giai đoạn 2] Models extracted from AdminController as a STANDALONE controller
/// (playbook §6 decision). Route strings unchanged: /api/v1/models...
/// Reference data — NO company-scoping (AssetModel has no CompanyId), NO output-cache
/// (pre-migration had none) → commands implement ILoggableCommand only (no cache marker).
/// GetById is NEW (playbook §6.5 pattern). Create/Update carry TODO BUG-H (no validation —
/// verbatim pre-migration behavior, see docs/BACKLOG.md).
/// </summary>
[ApiController, Route("api/v1/models"), Authorize]
public class ModelsController : ControllerBase
{
    private readonly IMediator _mediator;
    public ModelsController(IMediator mediator)
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

    [HttpGet, Authorize(Policy = "models.view")]
    public async Task<IActionResult> GetModels()
    {
        var list = await _mediator.Send(new ListModelsQuery());
        return Ok(new { status = "success", data = list });
    }

    [HttpGet("{id:guid}"), Authorize(Policy = "models.view")]
    public async Task<IActionResult> GetModel(Guid id)
    {
        var m = await _mediator.Send(new GetModelByIdQuery(id));
        if (m is null)
            return NotFound(new { status = "error", message = "Not found." });
        return Ok(new { status = "success", data = m });
    }

    [HttpPost, Authorize(Policy = "models.create")]
    public async Task<IActionResult> CreateModel([FromBody] CreateModelRequest r)
    {
        var result = await _mediator.Send(new CreateModelCommand(
            r.Name, r.ModelNumber, r.ManufacturerId, r.CategoryId, r.DepreciationId, r.FieldsetId,
            r.Eol, r.Notes, r.Requestable, GetCurrentUserId()));

        if (!result.Success)
            return MapFailure(result);

        return Ok(new { status = "success", data = new { Id = result.ModelId } });
    }

    [HttpPut("{id:guid}"), Authorize(Policy = "models.edit")]
    public async Task<IActionResult> UpdateModel(Guid id, [FromBody] UpdateModelRequest r)
    {
        var result = await _mediator.Send(new UpdateModelCommand(
            id, r.Name, r.ModelNumber, r.ManufacturerId, r.CategoryId, r.DepreciationId, r.FieldsetId,
            r.Eol, r.Notes, r.Requestable, GetCurrentUserId()));

        if (!result.Success)
            return MapFailure(result);

        return Ok(new { status = "success", message = "Updated." });
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = "models.delete")]
    public async Task<IActionResult> DeleteModel(Guid id)
    {
        var result = await _mediator.Send(new DeleteModelCommand(id, GetCurrentUserId()));

        if (!result.Success)
            return MapFailure(result);

        return Ok(new { status = "success", message = "Deleted." });
    }

    /// <summary>
    /// Maps a ModelResult failure to the EXACT same HTTP bodies as the pre-migration controller:
    /// NOT_FOUND → 404 without error_code; null ErrorCode (has-Assets guard) → 400 WITHOUT
    /// error_code (old body had none).
    /// </summary>
    private IActionResult MapFailure(ModelResult result)
    {
        if (result.ErrorCode == "NOT_FOUND")
            return NotFound(new { status = "error", message = result.Message });

        object body = result.ErrorCode is null
            ? new { status = "error", message = result.Message }
            : new { status = "error", message = result.Message, error_code = result.ErrorCode };
        return BadRequest(body);
    }
}

/// <summary>Request DTO for POST /api/v1/models (was previously mass-bound to the AssetModel entity —
/// narrowing also removes the client-set-Id quirk, see BUG-H #1 in docs/BACKLOG.md).</summary>
public record CreateModelRequest(
    string Name, string? ModelNumber, Guid? ManufacturerId, Guid? CategoryId, Guid? DepreciationId,
    Guid? FieldsetId, int? Eol, string? Notes, bool Requestable);

/// <summary>Patch-style Update DTO for AssetModel (Task M2) — nullable so a partial payload only changes sent fields. Moved from AdminController.cs.</summary>
public record UpdateModelRequest(
    string? Name = null, string? ModelNumber = null, Guid? ManufacturerId = null, Guid? CategoryId = null,
    Guid? DepreciationId = null, Guid? FieldsetId = null, int? Eol = null, string? Notes = null, bool? Requestable = null);
