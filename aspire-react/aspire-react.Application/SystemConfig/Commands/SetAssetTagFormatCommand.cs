using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Domain.SystemConfig;
using aspire_react.Server.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.SystemConfig.Commands;

/// <summary>
/// [FIX B 2026-10-02] Shared result for the two system-config writes. <c>Changed=false</c> is the
/// no-op case (submitted value identical to the stored one): the endpoint answers 200 with the same
/// message but NOTHING is written and NOTHING is logged — verbatim the old controller behaviour (an
/// admin hitting Save without editing must not spam identical audit rows).
/// <c>ItemId</c> is the SystemSetting row id, needed by BuildLogEntry (the behavior logs AFTER the
/// handler returned, so the id travels through the response).
/// </summary>
public record SystemConfigResult(
    bool Success,
    string Message,
    bool Changed,
    Guid? ItemId = null,
    string? Note = null,
    string? LogMeta = null);

/// <summary>
/// [FIX B 2026-10-02] PUT /api/v1/system/config/asset-tag-format (policy `system.config`) — moved
/// out of the controller so the controller is a thin IMediator map. The ActionLog is now produced by
/// ActionLogBehavior (<see cref="ILoggableCommand{TResponse}"/>) in the SAME transaction as the
/// config change (the old controller staged log + SaveChanges by hand: same guarantee, less glue).
/// Validation uses the Domain contract <see cref="AssetTagFormat"/> — the same rules
/// AssetTagGenerator.SetFormatAsync has always enforced.
/// </summary>
public record SetAssetTagFormatCommand(string? Format, Guid CurrentUserId)
    : IRequest<SystemConfigResult>, ILoggableCommand<SystemConfigResult>
{
    public ActionLogEntry? BuildLogEntry(SystemConfigResult response)
    {
        // No log for failures AND for the no-op case (changed = false).
        if (!response.Success || !response.Changed || response.ItemId is null) return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.SystemSetting,
            ItemId = response.ItemId.Value,
            ActionType = ActionType.Update,
            CreatedBy = CurrentUserId,
            CompanyId = null, // global system configuration — intentionally not company-scoped
            Note = response.Note,
            LogMeta = response.LogMeta
        };
    }
}

public class SetAssetTagFormatCommandHandler : IRequestHandler<SetAssetTagFormatCommand, SystemConfigResult>
{
    private readonly IApplicationDbContext _context;

    public SetAssetTagFormatCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<SystemConfigResult> Handle(SetAssetTagFormatCommand request, CancellationToken cancellationToken)
    {
        // Validate with the SAME rules the generator has always enforced (shared Domain contract).
        var trimmed = request.Format?.Trim();
        try
        {
            AssetTagFormat.Validate(trimmed);
        }
        catch (ArgumentException ex)
        {
            return new SystemConfigResult(false, ex.Message, false);
        }

        var setting = await _context.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == AssetTagFormat.SettingKey, cancellationToken);
        var oldValue = string.IsNullOrWhiteSpace(setting?.Value) ? AssetTagFormat.DefaultFormat : setting!.Value;

        // No-op guard: an unchanged value must NOT be written or logged.
        if (string.Equals(oldValue, trimmed, StringComparison.Ordinal))
            return new SystemConfigResult(true, "Đã lưu cấu hình.", false);

        if (setting == null)
        {
            setting = new SystemSetting
            {
                Key = AssetTagFormat.SettingKey,
                Value = trimmed!,
                Description = AssetTagFormat.Description,
                UpdatedBy = request.CurrentUserId
            };
            _context.SystemSettings.Add(setting);
        }
        else
        {
            setting.Value = trimmed!;
            setting.UpdatedBy = request.CurrentUserId;
            setting.UpdatedAt = DateTime.UtcNow;
        }

        // ActionLog comes from ActionLogBehavior (ILoggableCommand) — same transaction as this save.
        await _context.SaveChangesAsync(cancellationToken);

        return new SystemConfigResult(
            true,
            "Đã lưu cấu hình.",
            true,
            ItemId: setting.Id,
            Note: $"Cập nhật format tự sinh Mã tài sản (Asset Tag): \"{oldValue}\" → \"{trimmed}\"",
            LogMeta: JsonSerializer.Serialize(new { changes = new { format = new { old = oldValue, @new = trimmed } } }));
    }
}

/// <summary>
/// [FIX B 2026-10-02] PUT /api/v1/system/config/passkeys-enabled (policy `system.config`) — the
/// AUTH Phase 3 passkey flag. Same structure/no-op/log rules as the asset-tag format command.
/// </summary>
public record SetPasskeysEnabledCommand(bool Enabled, Guid CurrentUserId)
    : IRequest<SystemConfigResult>, ILoggableCommand<SystemConfigResult>
{
    public ActionLogEntry? BuildLogEntry(SystemConfigResult response)
    {
        if (!response.Success || !response.Changed || response.ItemId is null) return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.SystemSetting,
            ItemId = response.ItemId.Value,
            ActionType = ActionType.Update,
            CreatedBy = CurrentUserId,
            CompanyId = null, // global system configuration — intentionally not company-scoped
            Note = response.Note,
            LogMeta = response.LogMeta
        };
    }
}

public class SetPasskeysEnabledCommandHandler : IRequestHandler<SetPasskeysEnabledCommand, SystemConfigResult>
{
    private const string PasskeysDescription = "Bật/tắt đăng nhập bằng Passkey (WebAuthn) — AUTH Phase 3";

    private readonly IApplicationDbContext _context;

    public SetPasskeysEnabledCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<SystemConfigResult> Handle(SetPasskeysEnabledCommand request, CancellationToken cancellationToken)
    {
        var newValue = request.Enabled ? "true" : "false";
        var setting = await _context.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == IWebAuthnService.PasskeysEnabledSettingKey, cancellationToken);
        var oldValue = setting?.Value ?? "false";

        // Same no-op guard as asset-tag-format: unchanged value → no write, no audit row.
        if (string.Equals(oldValue, newValue, StringComparison.OrdinalIgnoreCase))
            return new SystemConfigResult(true, "Đã lưu cấu hình.", false);

        if (setting == null)
        {
            setting = new SystemSetting
            {
                Key = IWebAuthnService.PasskeysEnabledSettingKey,
                Value = newValue,
                Description = PasskeysDescription,
                UpdatedBy = request.CurrentUserId
            };
            _context.SystemSettings.Add(setting);
        }
        else
        {
            setting.Value = newValue;
            setting.UpdatedBy = request.CurrentUserId;
            setting.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new SystemConfigResult(
            true,
            "Đã lưu cấu hình.",
            true,
            ItemId: setting.Id,
            Note: $"Bật đăng nhập bằng Passkey: {oldValue} → {newValue}",
            LogMeta: JsonSerializer.Serialize(new { changes = new { enabled = new { old = oldValue, @new = newValue } } }));
    }
}
