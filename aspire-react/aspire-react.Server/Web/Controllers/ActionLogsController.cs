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
/// 404 bodies Vietnamese; by-system paging clamps; policy "assets.view" on by-system only.
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
    [Authorize(Policy = "assets.view")]
    public async Task<IActionResult> GetBySystem(
        [FromQuery] Guid systemInfoId,
        [FromQuery] Guid? systemPositionId = null,
        [FromQuery] ActionType? actionType = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _mediator.Send(new GetBySystemQuery(systemInfoId, systemPositionId, actionType, from, to, page, pageSize));
        if (result == null)
            return NotFound(new { status = "error", message = "Hệ thống không tồn tại hoặc ngoài phạm vi công ty của bạn." });

        return Ok(new { status = "success", data = result.Items, total = result.Total });
    }
}
