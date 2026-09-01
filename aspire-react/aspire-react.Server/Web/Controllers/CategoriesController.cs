using System.Security.Claims;
using aspire_react.Server.Application.Categories.Commands;
using aspire_react.Server.Application.Categories.Queries;
using aspire_react.Server.Application.Common;
using aspire_react.Server.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// [Giai đoạn 2] Categories extracted from AdminController as a STANDALONE controller
/// (playbook §6 decision: mỗi section = 1 controller riêng — precedent cho
/// Location/Manufacturer/Supplier). Route strings unchanged: /api/v1/categories...
/// No company-scoping (Category is global reference data, no CompanyId).
/// Create/Update/Delete dispatch Commands implementing BOTH ILoggableCommand (log via
/// ActionLogBehavior, enrichment 2a) and ICacheInvalidatingCommand (evict ref:categories via
/// CacheInvalidationBehavior) — the manual Log(entry) + InvalidateCategoriesAsync calls are gone.
/// GetById is NEW (was missing pre-migration — small feature addition, approved).
/// </summary>
[ApiController, Route("api/v1/categories"), Authorize]
public class CategoriesController : ControllerBase
{
    private readonly IMediator _mediator;
    public CategoriesController(IMediator mediator)
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

    [HttpGet, Authorize(Policy = "categories.view")]
    [OutputCache(PolicyName = "RefData", Tags = [CacheTags.Categories])] // Task P: reference-data, non-company-scoped (no CompanyId), same for all authorized users
    public async Task<IActionResult> GetCategories([FromQuery] CategoryType? type)
    {
        var list = await _mediator.Send(new ListCategoriesQuery(type));
        return Ok(new { status = "success", data = list });
    }

    [HttpGet("{id:guid}"), Authorize(Policy = "categories.view")]
    public async Task<IActionResult> GetCategory(Guid id)
    {
        var c = await _mediator.Send(new GetCategoryByIdQuery(id));
        if (c is null)
            return NotFound(new { status = "error", message = "Category not found." });
        return Ok(new { status = "success", data = c });
    }

    [HttpPost, Authorize(Policy = "categories.create")]
    public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryRequest r)
    {
        var result = await _mediator.Send(new CreateCategoryCommand(
            r.Name, r.CategoryType, r.TagColor, r.CheckinEmail, r.RequireAcceptance, r.UseDefaultEula, r.Notes,
            GetCurrentUserId()));

        if (!result.Success)
            return MapFailure(result);

        return Ok(new { status = "success", data = new { Id = result.CategoryId, Name = result.Name } });
    }

    [HttpPut("{id:guid}"), Authorize(Policy = "categories.edit")]
    public async Task<IActionResult> UpdateCategory(Guid id, [FromBody] UpdateCategoryRequest r)
    {
        var result = await _mediator.Send(new UpdateCategoryCommand(
            id, r.Name, r.TagColor, r.CheckinEmail, r.RequireAcceptance, r.UseDefaultEula, r.Notes,
            GetCurrentUserId()));

        if (!result.Success)
            return MapFailure(result);

        return Ok(new { status = "success", message = "Updated." });
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = "categories.delete")]
    public async Task<IActionResult> DeleteCategory(Guid id)
    {
        var result = await _mediator.Send(new DeleteCategoryCommand(id, GetCurrentUserId()));

        if (!result.Success)
            return MapFailure(result);

        return Ok(new { status = "success", message = "Deleted." });
    }

    /// <summary>
    /// Maps a CategoryResult failure to the EXACT same HTTP bodies as the pre-migration
    /// AdminController actions: NOT_FOUND → 404 without error_code; null ErrorCode → 400 without
    /// error_code (old bodies had no error_code key); CATEGORY_IN_USE → 400 with error_code.
    /// </summary>
    private IActionResult MapFailure(CategoryResult result)
    {
        if (result.ErrorCode == "NOT_FOUND")
            return NotFound(new { status = "error", message = result.Message });

        object body = result.ErrorCode is null
            ? new { status = "error", message = result.Message }
            : new { status = "error", message = result.Message, error_code = result.ErrorCode };
        return BadRequest(body);
    }
}

/// <summary>Request DTO for POST /api/v1/categories (was previously mass-bound to the Category entity — narrowed to business fields).</summary>
public record CreateCategoryRequest(
    string Name,
    CategoryType CategoryType,
    string? TagColor,
    bool CheckinEmail,
    bool RequireAcceptance,
    bool UseDefaultEula,
    string? Notes);

/// <summary>Patch-style Update DTO for Category (Task M2) — nullable so a partial payload only changes sent fields. Moved from AdminController.cs.</summary>
public record UpdateCategoryRequest(
    string? Name = null, string? TagColor = null, bool? CheckinEmail = null, bool? RequireAcceptance = null,
    bool? UseDefaultEula = null, string? Notes = null);
