using System.Security.Claims;
using aspire_react.Server.Application.SystemConfig.Commands;
using aspire_react.Server.Application.SystemConfig.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// [FIX B 2026-10-02] THIN 100% — this was the last controller without <see cref="IMediator"/>
/// (the audit found it running EF + a manual ActionLog directly). Every endpoint is now one Send:
///   * GET  asset-tag-format / passkeys-enabled → Queries (any authenticated user);
///   * PUT  asset-tag-format / passkeys-enabled → Commands (policy `system.config`), each an
///     ILoggableCommand ⇒ the audit entry is committed together with the config change, and an
///     unchanged value stays a no-op (no write, no log) exactly as before.
/// </summary>
[ApiController, Route("api/v1/system/config")]
public class SystemConfigController : ControllerBase
{
    private readonly IMediator _mediator;

    public SystemConfigController(IMediator mediator)
    {
        _mediator = mediator;
    }

    private Guid GetCurrentUserId()
    {
        // [SEC-FIX CLAIM-CLEANUP, 2026-08-23] ONLY "local_user_id" is a user identity source.
        // Keycloak sub/preferred_username are never a user identity source. Absent → Guid.Empty.
        if (Guid.TryParse(User.FindFirstValue("local_user_id"), out var local)) return local;
        return Guid.Empty;
    }

    // GET the Asset Tag auto-generation format (readable by any authenticated user so the create form
    // can show the hint). The PUT below is gated by system.config.
    [HttpGet("asset-tag-format")]
    [Authorize]
    public async Task<IActionResult> GetAssetTagFormat(CancellationToken ct)
    {
        var format = await _mediator.Send(new GetAssetTagFormatQuery(), ct);
        return Ok(new { status = "success", data = new { format } });
    }

    [HttpPut("asset-tag-format")]
    [Authorize(Policy = "system.config")]
    public async Task<IActionResult> SetAssetTagFormat([FromBody] SetAssetTagFormatRequest r, CancellationToken ct)
    {
        var result = await _mediator.Send(new SetAssetTagFormatCommand(r.Format, GetCurrentUserId()), ct);

        // Verbatim body of the pre-migration controller: validation failure → 400 {status,message}
        // (no error_code); success (changed OR no-op) → 200 with the same message.
        if (!result.Success)
            return BadRequest(new { status = "error", message = result.Message });

        return Ok(new { status = "success", message = result.Message });
    }

    // ==================== [AUTH Phase 3] Passkey flag (auth.passkeys.enabled) ====================

    // GET readable by any authenticated user (AccountPage uses it to gate the register UI).
    [HttpGet("passkeys-enabled")]
    [Authorize]
    public async Task<IActionResult> GetPasskeysEnabled(CancellationToken ct)
    {
        var enabled = await _mediator.Send(new GetPasskeysEnabledQuery(), ct);
        return Ok(new { status = "success", data = new { enabled } });
    }

    [HttpPut("passkeys-enabled")]
    [Authorize(Policy = "system.config")]
    public async Task<IActionResult> SetPasskeysEnabled([FromBody] SetPasskeysEnabledRequest r, CancellationToken ct)
    {
        var result = await _mediator.Send(new SetPasskeysEnabledCommand(r.Enabled, GetCurrentUserId()), ct);

        if (!result.Success)
            return BadRequest(new { status = "error", message = result.Message });

        return Ok(new { status = "success", message = result.Message });
    }
}

public record SetAssetTagFormatRequest(string Format);

public record SetPasskeysEnabledRequest(bool Enabled);
