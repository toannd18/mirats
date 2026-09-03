using System.Security.Claims;
using aspire_react.Server.Application.Common;
using aspire_react.Server.Application.Companies.Commands;
using aspire_react.Server.Application.Companies.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// [Giai đoạn 3] Companies migrated to MediatR — the tree/scope reference-data controller
/// (Task V subtree read-scoping + SEC-FIX S5 write-scoping + SEC-FIX AR-2 full delete audit).
/// Routes unchanged: /api/v1/companies... Create/Update/Delete = ILoggableCommand (thin log →
/// enrichment 2a) + ICacheInvalidatingCommand (ref:companies, success-only — replaces the manual
/// _cacheInvalidator.InvalidateCompaniesAsync() calls). GetAll keeps the RefDataCompanyScope
/// OutputCache attribute VERBATIM on the action (response caching is an HTTP concern).
/// </summary>
[ApiController, Route("api/v1/companies"), Authorize(Policy = "companies.view")]
public class CompaniesController : ControllerBase
{
    private readonly IMediator _mediator;
    public CompaniesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    private Guid GetCurrentUserId()
    {
        // [SEC-FIX CLAIM-CLEANUP, 2026-08-23] ONLY the local DB user id stamped by JIT
        // provisioning ("local_user_id") is used — Keycloak sub/preferred_username are never a
        // user identity source (bug-class 1; parsing `sub` returns the WRONG id). Absent claim →
        // Guid.Empty (fail closed), matching the CompanyScopeService pattern.
        if (Guid.TryParse(User.FindFirstValue("local_user_id"), out var local)) return local;
        return Guid.Empty;
    }

    // GET — returns flat list grouped into a tree, company-scoped per user (Task V).
    [HttpGet]
    [OutputCache(PolicyName = "RefDataCompanyScope", Tags = [CacheTags.Companies])] // Task V: cache key varies by company scope → per-scope isolation
    public async Task<IActionResult> GetAll()
    {
        var roots = await _mediator.Send(new ListCompaniesQuery());
        return Ok(new { status = "success", data = roots });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var c = await _mediator.Send(new GetCompanyByIdQuery(id));
        if (c == null) return NotFound(new { status = "error", message = "Not found" });
        return Ok(new { status = "success", data = new { c.Id, c.Name, c.Code, c.ParentId, Children = new List<object>() } });
    }

    [HttpPost, Authorize(Policy = "companies.create")]
    public async Task<IActionResult> Create([FromBody] CompanyDto dto)
    {
        var result = await _mediator.Send(new CreateCompanyCommand(dto.Name, dto.ParentId, dto.Code, GetCurrentUserId()));
        if (!result.Success)
            return MapFailure(result);
        // Verbatim pre-migration shape: { id, name, code, parentId } (explicit names — the raw
        // result record would serialize `CompanyId` as `companyId`, breaking response parity).
        return Ok(new { status = "success", data = new { Id = result.CompanyId, Name = result.Name, Code = result.Code, ParentId = result.ParentId } });
    }

    [HttpPut("{id:guid}"), Authorize(Policy = "companies.edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CompanyDto dto)
    {
        var result = await _mediator.Send(new UpdateCompanyCommand(id, dto.Name, dto.ParentId, dto.Code, GetCurrentUserId()));
        if (!result.Success)
            return MapFailure(result);
        return Ok(new { status = "success", message = "Updated" });
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = "companies.delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await _mediator.Send(new DeleteCompanyCommand(id, GetCurrentUserId()));
        if (!result.Success)
            return MapFailure(result);
        return Ok(new { status = "success", message = "Deleted" });
    }

    /// <summary>
    /// Maps a CompanyResult failure to the EXACT same HTTP bodies as the pre-migration controller:
    /// NOT_FOUND → 404 without error_code; null ErrorCode (NOCO/dup-code/circular/has-children) →
    /// 400 WITHOUT error_code; COMPANY_IN_USE → 400 WITH error_code.
    /// </summary>
    private IActionResult MapFailure(CompanyResult result)
    {
        if (result.ErrorCode == "NOT_FOUND")
            return NotFound(new { status = "error", message = result.Message });

        object body = result.ErrorCode is null
            ? new { status = "error", message = result.Message }
            : new { status = "error", message = result.Message, error_code = result.ErrorCode };
        return BadRequest(body);
    }
}

/// <summary>Request DTO for POST/PUT /api/v1/companies — verbatim from the pre-migration record.</summary>
public record CompanyDto(string Name, Guid? ParentId, string? Code = null);
