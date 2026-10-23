using System.Security.Claims;
using aspire_react.Server.Application.Depreciations.Commands;
using aspire_react.Server.Application.Depreciations.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// [Giai đoạn 2-cuối] Depreciations extracted from AdminController as a STANDALONE controller —
/// the LAST section: with this, AdminController.cs is fully dissolved and DELETED (empty class
/// had no remaining purpose). Route strings unchanged: /api/v1/depreciations...
/// GET + a minimal Create path used for admin setup/fixtures (the permission key already existed).
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

    /// <summary>
    /// [UI-3D] Minimal reference-data create path used by admin setup/fixtures. The permission
    /// key already existed (depreciations.create) but no endpoint had ever been exposed.
    /// </summary>
    [HttpPost, Authorize(Policy = "depreciations.create")]
    public async Task<IActionResult> Create([FromBody] CreateDepreciationRequest request)
    {
        var currentUserId = Guid.TryParse(User.FindFirstValue("local_user_id"), out var local)
            ? local
            : Guid.Empty;
        var result = await _mediator.Send(new CreateDepreciationCommand(request.Name, request.Months, currentUserId));
        if (!result.Success)
        {
            return BadRequest(new
            {
                status = "error",
                message = result.Message,
                error_code = result.ErrorCode,
            });
        }
        return Ok(new { status = "success", message = result.Message, data = new { id = result.Id } });
    }
}

public record CreateDepreciationRequest(string Name, int Months);
