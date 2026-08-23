using System.Security.Claims;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

[ApiController, Route("api/v1/system/config")]
public class SystemConfigController : ControllerBase
{
    private readonly IAssetTagGenerator _assetTagGenerator;

    public SystemConfigController(IAssetTagGenerator assetTagGenerator)
    {
        _assetTagGenerator = assetTagGenerator;
    }

    private Guid GetCurrentUserId()
    {
        // [SEC-FIX CLAIM-CLEANUP, 2026-08-23] ONLY "local_user_id" (JIT-stamped). Keycloak
        // sub/preferred_username are never a user identity source (bug-class 1). Absent → Guid.Empty.
        if (Guid.TryParse(User.FindFirstValue("local_user_id"), out var local)) return local;
        return Guid.Empty;
    }

    // GET the Asset Tag auto-generation format (readable by any authenticated user so the create form
    // can show the hint). The PUT below is gated by system.config.
    [HttpGet("asset-tag-format")]
    [Authorize]
    public async Task<IActionResult> GetAssetTagFormat(CancellationToken ct)
    {
        var format = await _assetTagGenerator.GetFormatAsync(ct);
        return Ok(new { status = "success", data = new { format } });
    }

    [HttpPut("asset-tag-format")]
    [Authorize(Policy = "system.config")]
    public async Task<IActionResult> SetAssetTagFormat([FromBody] SetAssetTagFormatRequest r, CancellationToken ct)
    {
        try
        {
            await _assetTagGenerator.SetFormatAsync(r.Format, GetCurrentUserId(), ct);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { status = "error", message = ex.Message });
        }
        return Ok(new { status = "success", message = "Đã lưu cấu hình." });
    }
}

public record SetAssetTagFormatRequest(string Format);
