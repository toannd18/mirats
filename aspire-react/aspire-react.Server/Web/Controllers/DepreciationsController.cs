using aspire_react.Server.Application.Depreciations.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// [Giai đoạn 2-cuối] Depreciations extracted from AdminController as a STANDALONE controller —
/// the LAST section: with this, AdminController.cs is fully dissolved and DELETED (empty class
/// had no remaining purpose). Route strings unchanged: /api/v1/depreciations...
/// GET-only resource (no Create/Update/Delete endpoints have ever existed) → Queries only,
/// NO Behaviors (nothing to log — no commands; no cache — endpoint was never output-cached).
/// No company-scoping — Depreciation has no CompanyId (reference data, by design).
/// </summary>
[ApiController, Route("api/v1/depreciations"), Authorize]
public class DepreciationsController : ControllerBase
{
    private readonly IMediator _mediator;
    public DepreciationsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet, Authorize(Policy = "depreciations.view")]
    public async Task<IActionResult> GetDepreciations()
    {
        var list = await _mediator.Send(new ListDepreciationsQuery());
        return Ok(new { status = "success", data = list });
    }

    [HttpGet("{id:guid}"), Authorize(Policy = "depreciations.view")]
    public async Task<IActionResult> GetDepreciation(Guid id)
    {
        var d = await _mediator.Send(new GetDepreciationByIdQuery(id));
        if (d is null)
            return NotFound(new { status = "error", message = "Not found." });
        return Ok(new { status = "success", data = d });
    }
}
