using System.Security.Claims;
using aspire_react.Server.Application.Common;
using aspire_react.Server.Application.Suppliers.Commands;
using aspire_react.Server.Application.Suppliers.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// [Giai đoạn 2] Suppliers extracted from AdminController as a STANDALONE controller
/// (playbook §6 decision). Route strings unchanged: /api/v1/suppliers...
/// Reference data — NO company-scoping (no CompanyId, by design — NOT a bug).
/// Create/Update/Delete dispatch Commands implementing BOTH ILoggableCommand (log thin →
/// enrichment 2a) and ICacheInvalidatingCommand (evict ref:suppliers). GetById is NEW
/// (playbook §6.5 pattern).
/// </summary>
[ApiController, Route("api/v1/suppliers"), Authorize]
public class SuppliersController : ControllerBase
{
    private readonly IMediator _mediator;
    public SuppliersController(IMediator mediator)
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

    [HttpGet, Authorize(Policy = "suppliers.view")]
    [OutputCache(PolicyName = "RefData", Tags = [CacheTags.Suppliers])] // Task P: reference-data, no CompanyId, same for all authorized users
    public async Task<IActionResult> GetSuppliers()
    {
        var list = await _mediator.Send(new ListSuppliersQuery());
        return Ok(new { status = "success", data = list });
    }

    [HttpGet("{id:guid}"), Authorize(Policy = "suppliers.view")]
    public async Task<IActionResult> GetSupplier(Guid id)
    {
        var s = await _mediator.Send(new GetSupplierByIdQuery(id));
        if (s is null)
            return NotFound(new { status = "error", message = "Not found." });
        return Ok(new { status = "success", data = s });
    }

    [HttpPost, Authorize(Policy = "suppliers.create")]
    public async Task<IActionResult> CreateSupplier([FromBody] CreateSupplierRequest r)
    {
        var result = await _mediator.Send(new CreateSupplierCommand(
            r.Code, r.Name, r.Url, r.Address, r.City, r.State, r.Country, r.Zip, r.Phone, r.Fax,
            r.ContactName, r.ContactEmail, GetCurrentUserId()));

        if (!result.Success)
            return MapFailure(result);

        return Ok(new { status = "success", data = new { Id = result.SupplierId, Code = result.Code, Name = result.Name } });
    }

    [HttpPut("{id:guid}"), Authorize(Policy = "suppliers.edit")]
    public async Task<IActionResult> UpdateSupplier(Guid id, [FromBody] UpdateSupplierRequest r)
    {
        var result = await _mediator.Send(new UpdateSupplierCommand(
            id, r.Code, r.Name, r.Url, r.Address, r.City, r.State, r.Country, r.Zip, r.Phone, r.Fax,
            r.ContactName, r.ContactEmail, GetCurrentUserId()));

        if (!result.Success)
            return MapFailure(result);

        return Ok(new { status = "success", message = "Updated." });
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = "suppliers.delete")]
    public async Task<IActionResult> DeleteSupplier(Guid id)
    {
        var result = await _mediator.Send(new DeleteSupplierCommand(id, GetCurrentUserId()));

        if (!result.Success)
            return MapFailure(result);

        return Ok(new { status = "success", message = "Deleted." });
    }

    /// <summary>
    /// Maps a SupplierResult failure to the EXACT same HTTP bodies as the pre-migration
    /// controller: NOT_FOUND → 404 without error_code; null ErrorCode (Code length / dup rules)
    /// → 400 without error_code (old bodies had none); SUPPLIER_IN_USE → 400 with error_code.
    /// </summary>
    private IActionResult MapFailure(SupplierResult result)
    {
        if (result.ErrorCode == "NOT_FOUND")
            return NotFound(new { status = "error", message = result.Message });

        object body = result.ErrorCode is null
            ? new { status = "error", message = result.Message }
            : new { status = "error", message = result.Message, error_code = result.ErrorCode };
        return BadRequest(body);
    }
}

/// <summary>Request DTO for POST /api/v1/suppliers (was previously mass-bound to the Supplier entity).</summary>
public record CreateSupplierRequest(
    string Code, string Name, string? Url, string? Address, string? City, string? State,
    string? Country, string? Zip, string? Phone, string? Fax, string? ContactName, string? ContactEmail);

/// <summary>Patch-style Update DTO for Supplier — nullable fields (Task M2 semantics preserved).</summary>
public record UpdateSupplierRequest(
    string? Code, string? Name, string? Url, string? Address, string? City, string? State,
    string? Country, string? Zip, string? Phone, string? Fax, string? ContactName, string? ContactEmail);
