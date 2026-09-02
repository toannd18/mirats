using System.Security.Claims;
using aspire_react.Server.Application.CustomFields.Commands;
using aspire_react.Server.Application.CustomFields.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// [Giai đoạn 3] CustomFields migrated to MediatR — controller giữ nguyên route
/// /api/v1/custom-fields..., per-action policies (KHÔNG có class-level [Authorize] — verbatim),
/// CreatedAtAction trong Create giữ nguyên. Commands: Create/Update/Delete implement
/// ILoggableCommand (thin log → enrichment 2a); KHÔNG ICacheInvalidatingCommand (no output-cache).
/// ⚠️ Create/Update giữ nguyên hành vi cũ: không empty-name check, Update FULL-PUT ×8 +
/// không dup-Slug (BUG-I — docs/BACKLOG.md), verbatim.
/// </summary>
[ApiController]
[Route("api/v1/custom-fields")]
public class CustomFieldsController : ControllerBase
{
    private readonly IMediator _mediator;
    public CustomFieldsController(IMediator mediator)
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

    [HttpGet]
    [Authorize(Policy = "customfields.view")]
    public async Task<IActionResult> GetFields()
    {
        var fields = await _mediator.Send(new ListCustomFieldsQuery());
        return Ok(new { status = "success", data = fields });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "customfields.view")]
    public async Task<IActionResult> GetField(Guid id)
    {
        var f = await _mediator.Send(new GetCustomFieldByIdQuery(id));
        if (f == null) return NotFound(new { status = "error", message = "Custom field not found." });
        return Ok(new { status = "success", data = f });
    }

    [HttpPost]
    [Authorize(Policy = "customfields.create")]
    public async Task<IActionResult> Create([FromBody] CreateCustomFieldRequest r)
    {
        var result = await _mediator.Send(new CreateCustomFieldCommand(
            r.Name, r.Slug, r.Format, r.Element, r.FieldValues, r.FieldEncrypted, r.HelpText, r.IsUnique,
            GetCurrentUserId()));

        if (!result.Success)
            return MapFailure(result);

        return CreatedAtAction(nameof(GetField), new { id = result.CustomFieldId },
            new { status = "success", message = result.Message, data = new { Id = result.CustomFieldId, Name = result.Name } });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "customfields.edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateCustomFieldRequest r)
    {
        var result = await _mediator.Send(new UpdateCustomFieldCommand(
            id, r.Name, r.Slug, r.Format, r.Element, r.FieldValues, r.FieldEncrypted, r.HelpText, r.IsUnique,
            GetCurrentUserId()));

        if (!result.Success)
            return MapFailure(result);

        return Ok(new { status = "success", message = "Custom field updated." });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "customfields.delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await _mediator.Send(new DeleteCustomFieldCommand(id, GetCurrentUserId()));

        if (!result.Success)
            return MapFailure(result);

        return Ok(new { status = "success", message = "Custom field deleted." });
    }

    /// <summary>
    /// Maps a CustomFieldResult failure to the EXACT same HTTP bodies as the pre-migration
    /// controller: NOT_FOUND → 404 without error_code; null ErrorCode (dup-slug Create rule) →
    /// 400 WITHOUT error_code (old body had none); CUSTOM_FIELD_IN_USE → 400 WITH error_code.
    /// </summary>
    private IActionResult MapFailure(CustomFieldResult result)
    {
        if (result.ErrorCode == "NOT_FOUND")
            return NotFound(new { status = "error", message = result.Message });

        object body = result.ErrorCode is null
            ? new { status = "error", message = result.Message }
            : new { status = "error", message = result.Message, error_code = result.ErrorCode };
        return BadRequest(body);
    }
}

/// <summary>Request DTO for POST and PUT /api/v1/custom-fields — verbatim field set from the pre-migration CreateCustomFieldRequest record (Update reused the SAME record — full-PUT semantics, see BUG-I).</summary>
public record CreateCustomFieldRequest(string Name, string Slug, string Format, string? Element,
    string? FieldValues, bool FieldEncrypted, string? HelpText, bool IsUnique);
