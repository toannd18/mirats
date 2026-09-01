using System.Security.Claims;
using aspire_react.Server.Application.Departments.Commands;
using aspire_react.Server.Application.Departments.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// [Giai đoạn 1 — pilot MediatR migration] All 5 actions now delegate to
/// Application/Departments Commands+Queries. No direct _context / ICompanyScopeService /
/// IActionLogService usage remains in this controller. HTTP shapes (status/message/error_code,
/// 200/400/404 mapping) are preserved EXACTLY from the pre-migration controller — see
/// docs/MEDIATR_MIGRATION_PLAYBOOK.md for the migration procedure.
/// </summary>
[ApiController, Route("api/v1/departments"), Authorize]
public class DepartmentsController : ControllerBase
{
    private readonly IMediator _mediator;
    public DepartmentsController(IMediator mediator)
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

    [HttpGet, Authorize(Policy = "departments.view")]
    public async Task<IActionResult> GetAll([FromQuery] Guid? companyId)
    {
        var list = await _mediator.Send(new ListDepartmentsQuery(companyId));
        return Ok(new { status = "success", data = list });
    }

    [HttpGet("{id:guid}"), Authorize(Policy = "departments.view")]
    public async Task<IActionResult> Get(Guid id)
    {
        var d = await _mediator.Send(new GetDepartmentByIdQuery(id));
        if (d is null)
            return NotFound(new { status = "error", message = "Not found." });
        return Ok(new { status = "success", data = d });
    }

    [HttpPost, Authorize(Policy = "departments.create")]
    public async Task<IActionResult> Create([FromBody] CreateDepartmentRequest r)
    {
        var result = await _mediator.Send(new CreateDepartmentCommand(
            r.Name, r.CompanyId, r.ManagerId, r.Phone, r.Fax, GetCurrentUserId()));

        if (!result.Success)
            return MapFailure(result);

        return Ok(new { status = "success", data = new { Id = result.DepartmentId, Name = result.Name } });
    }

    [HttpPut("{id:guid}"), Authorize(Policy = "departments.edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDepartmentRequest r)
    {
        var result = await _mediator.Send(new UpdateDepartmentCommand(
            id, r.Name, r.CompanyId, r.ManagerId, r.Phone, r.Fax, GetCurrentUserId()));

        if (!result.Success)
            return MapFailure(result);

        return Ok(new { status = "success", message = "Updated." });
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = "departments.delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await _mediator.Send(new DeleteDepartmentCommand(id, GetCurrentUserId()));

        if (!result.Success)
            return MapFailure(result);

        return Ok(new { status = "success", message = "Deleted." });
    }

    /// <summary>
    /// Maps a DepartmentResult failure to the EXACT same HTTP bodies as the pre-migration
    /// controller: NOT_FOUND → 404 without error_code; null ErrorCode → 400 without error_code
    /// (old bodies had no error_code key); other codes → 400 with error_code.
    /// </summary>
    private IActionResult MapFailure(DepartmentResult result)
    {
        if (result.ErrorCode == "NOT_FOUND")
            return NotFound(new { status = "error", message = result.Message });

        object body = result.ErrorCode is null
            ? new { status = "error", message = result.Message }
            : new { status = "error", message = result.Message, error_code = result.ErrorCode };
        return BadRequest(body);
    }
}

/// <summary>Request DTO for POST /api/v1/departments (was previously mass-bound to the Department entity).</summary>
public record CreateDepartmentRequest(string Name, Guid? CompanyId, Guid? ManagerId, string? Phone, string? Fax);

/// <summary>Request DTO for PUT /api/v1/departments/{id} (full-PUT semantics preserved).</summary>
public record UpdateDepartmentRequest(string Name, Guid? CompanyId, Guid? ManagerId, string? Phone, string? Fax);
