using System.Security.Claims;
using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Web.Controllers;

[ApiController, Route("api/v1/system/config")]
public class SystemConfigController : ControllerBase
{
    private readonly IAssetTagGenerator _assetTagGenerator;
    private readonly AppDbContext _context;
    private readonly IActionLogService _actionLogService;

    public SystemConfigController(IAssetTagGenerator assetTagGenerator, AppDbContext context, IActionLogService actionLogService)
    {
        _assetTagGenerator = assetTagGenerator;
        _context = context;
        _actionLogService = actionLogService;
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
        // Validate with the SAME rules SetFormatAsync has always enforced (shared static validator).
        var trimmed = r.Format?.Trim();
        try
        {
            AssetTagGenerator.ValidateFormat(trimmed);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { status = "error", message = ex.Message });
        }

        // [SEC-FIX A1, 2026-08-23] ActionLog for system config changes. Previously this PUT mutated
        // global configuration with NO audit trail. The write path is done HERE (instead of calling
        // SetFormatAsync) so the ActionLog is staged BEFORE a single SaveChanges — audit entry and
        // config change commit in the SAME transaction (convention: ActionLog same-transaction).
        var setting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == AssetTagGenerator.FormatSettingKey, ct);
        var oldValue = string.IsNullOrWhiteSpace(setting?.Value) ? AssetTagGenerator.DefaultFormat : setting!.Value;

        // No-op guard: an unchanged value must NOT be written or logged (an admin hitting Save
        // without editing anything would otherwise spam identical audit rows).
        if (string.Equals(oldValue, trimmed, StringComparison.Ordinal))
            return Ok(new { status = "success", message = "Đã lưu cấu hình." });

        var userId = GetCurrentUserId();
        if (setting == null)
        {
            setting = new SystemSetting
            {
                Key = AssetTagGenerator.FormatSettingKey,
                Value = trimmed!,
                Description = AssetTagGenerator.FormatDescription,
                UpdatedBy = userId
            };
            _context.SystemSettings.Add(setting);
        }
        else
        {
            setting.Value = trimmed!;
            setting.UpdatedBy = userId;
            setting.UpdatedAt = DateTime.UtcNow;
        }

        _actionLogService.LogAction(
            itemType: ItemType.SystemSetting,
            itemId: setting.Id,
            actionType: ActionType.Update,
            loggedByUserId: userId,
            companyId: null, // global system configuration — intentionally not company-scoped
            note: $"Cập nhật format tự sinh Mã tài sản (Asset Tag): \"{oldValue}\" → \"{trimmed}\"",
            logMeta: JsonSerializer.Serialize(new { changes = new { format = new { old = oldValue, @new = trimmed } } }));

        await _context.SaveChangesAsync(ct);
        return Ok(new { status = "success", message = "Đã lưu cấu hình." });
    }
}

public record SetAssetTagFormatRequest(string Format);
