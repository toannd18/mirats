using aspire_react.Server.Application.Labels.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// [Giai đoạn 3] Labels migrated to MediatR — QR label GENERATION endpoint (read-only:
/// no DB write, no ActionLog, no cache, no guard — verbatim pre-migration semantics).
/// Route unchanged: POST /api/v1/assets/labels (nested under the assets route — NOT /labels).
/// NO class-level [Authorize] (per-action policy only — verbatim). The controller resolves
/// the request base URL from HttpContext and passes it into the Query (handlers cannot
/// access HttpContext by design).
/// Frontend impact note: NO frontend caller exists for this endpoint (grep-verified) —
/// any pre-existing defect here would have zero user-facing impact.
/// </summary>
[ApiController]
[Route("api/v1/assets")]
public class LabelsController : ControllerBase
{
    private readonly IMediator _mediator;
    public LabelsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("labels")]
    [Authorize(Policy = "assets.view")]
    public async Task<IActionResult> GenerateLabels([FromBody] GenerateLabelsRequest request)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var labels = await _mediator.Send(new GenerateLabelsQuery(request.AssetIds, baseUrl));

        if (labels.Count == 0)
            return NotFound(new { status = "error", message = "No assets found." });

        return Ok(new { status = "success", data = labels });
    }
}

/// <summary>Request DTO for POST /api/v1/assets/labels — verbatim from the pre-migration record.</summary>
public record GenerateLabelsRequest(List<Guid> AssetIds);
