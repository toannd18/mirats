using aspire_react.Server.Application.Search;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// [UI-3C] Global quick search. Authorization and company scope are resolved on the server:
/// each result type is only queried when the caller has its .view policy, and every query is
/// limited to the caller's company scope (plus company-less records).
/// </summary>
[ApiController]
[Route("api/v1/search")]
[Authorize]
public class SearchController : ControllerBase
{
    private readonly IMediator _mediator;

    public SearchController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromServices] IAuthorizationService authorizationService)
    {
        var query = q?.Trim() ?? string.Empty;
        if (query.Length < 2 || query.Length > 64)
        {
            return BadRequest(new
            {
                status = "error",
                message = "Từ khóa tìm kiếm phải có từ 2 đến 64 ký tự.",
                error_code = "SEARCH_QUERY_INVALID",
            });
        }

        var permissions = new SearchPermissions(
            Assets: (await authorizationService.AuthorizeAsync(User, "assets.view")).Succeeded,
            Systems: (await authorizationService.AuthorizeAsync(User, "systems.view")).Succeeded,
            Consumables: (await authorizationService.AuthorizeAsync(User, "consumables.view")).Succeeded,
            Components: (await authorizationService.AuthorizeAsync(User, "components.view")).Succeeded,
            Accessories: (await authorizationService.AuthorizeAsync(User, "accessories.view")).Succeeded,
            Licenses: (await authorizationService.AuthorizeAsync(User, "licenses.view")).Succeeded,
            Users: (await authorizationService.AuthorizeAsync(User, "users.view")).Succeeded);

        var result = await _mediator.Send(new GlobalSearchQuery(query, permissions));
        return Ok(new { status = "success", data = result });
    }
}
