using aspire_react.Server.Application.ActionLogs.Queries;
using aspire_react.Server.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// [Giai đoạn 3] ActionLogs migrated to MediatR — 2 GET-only audit-history endpoints
/// (0 mutation, 0 ActionLog writes, no Commands → no markers). Routes unchanged:
/// /api/v1/action-logs...
/// Single-item visibility (IsItemVisibleAsync) moved VERBATIM into GetActionLogsQueryHandler
/// (duyệt phương án (a)) — a DIFFERENT operation from IActionLogVisibilityService's
/// bounded-list filter (Dashboard/Reports); no forced interface merge.
/// Parity quirks verbatim: superuser + unknown item → 200 with EMPTY data (visibility bypass);
/// 404 bodies Vietnamese; by-system paging clamps. by-system accepts EITHER
/// assets.view OR systems.view because the same history is opened from the System detail tab
/// and from the standalone /system-history route.
/// </summary>
[ApiController]
[Route("api/v1/action-logs")]
[Authorize]
public class ActionLogsController : ControllerBase
{
    private readonly IMediator _mediator;
    public ActionLogsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> GetActionLogs(
        [FromQuery] ItemType itemType,
        [FromQuery] Guid itemId)
    {
        var logs = await _mediator.Send(new GetActionLogsQuery(itemType, itemId));
        if (logs == null)
            return NotFound(new { status = "error", message = "Không tìm thấy lịch sử." });

        return Ok(new { status = "success", data = logs });
    }

    /// <summary>
    /// System history — every Asset action that targeted a SystemPosition belonging to one system.
    /// Reuses the same response shape as GET /action-logs, plus the resolved Item (Asset) display
    /// name so the reader knows which asset moved.
    /// </summary>
    [HttpGet("by-system")]
    public async Task<IActionResult> GetBySystem(
        [FromServices] IAuthorizationService authorizationService,
        [FromQuery] Guid systemInfoId,
        [FromQuery] Guid? systemPositionId = null,
        [FromQuery] ActionType? actionType = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        // One endpoint serves two entry points: System detail (systems.view) and the standalone
        // system-history screen (assets.view). Multiple [Authorize] attributes are AND, so the
        // OR is evaluated here through the registered permission policies (superuser bypasses both).
        var canViewAssets = (await authorizationService.AuthorizeAsync(User, "assets.view")).Succeeded;
        var canViewSystems = (await authorizationService.AuthorizeAsync(User, "systems.view")).Succeeded;
        if (!canViewAssets && !canViewSystems)
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                status = "error",
                message = "Bạn không có quyền xem lịch sử hệ thống.",
                error_code = "FORBIDDEN",
            });

        var result = await _mediator.Send(new GetBySystemQuery(systemInfoId, systemPositionId, actionType, from, to, page, pageSize));
        if (result == null)
            return NotFound(new { status = "error", message = "Hệ thống không tồn tại hoặc ngoài phạm vi công ty của bạn." });

        return Ok(new { status = "success", data = result.Items, total = result.Total });
    }
}
