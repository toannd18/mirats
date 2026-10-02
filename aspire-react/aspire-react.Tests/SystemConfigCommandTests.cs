using aspire_react.Server.Application.Common.Behaviors;
using aspire_react.Server.Application.SystemConfig.Commands;
using aspire_react.Server.Application.SystemConfig.Queries;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [FIX B 2026-10-02] SystemConfig endpoints migrated from a FAT controller (EF + manual ActionLog)
/// to MediatR. These tests pin the behaviour that had to survive the move:
///   * invalid format → 400 message (no write, no log) — same message as AssetTagGenerator;
///   * valid change → setting written + EXACTLY ONE ActionLog with the changes meta;
///   * unchanged value → 200 no-op, NO write, NO log (the old controller's no-op guard);
///   * passkey flag round-trip + defaults.
/// </summary>
public class SystemConfigCommandTests
{
    private static readonly Guid ActorId = Guid.NewGuid();

    private static AppDbContext CreateContext(string name) => TestHelpers.CreateContext(name);

    private static SetAssetTagFormatCommandHandler FormatHandler(AppDbContext ctx) => new(ctx);
    private static SetPasskeysEnabledCommandHandler PasskeysHandler(AppDbContext ctx) => new(ctx);

    // ==================== asset-tag-format ====================

    [Fact]
    public async Task SetAssetTagFormat_Invalid_Rejected_NoWrite_NoLog()
    {
        await using var ctx = CreateContext(nameof(SetAssetTagFormat_Invalid_Rejected_NoWrite_NoLog));
        var handler = FormatHandler(ctx);
        var actionLog = TestHelpers.CreateActionLogService(ctx, ActorId);
        var behavior = new ActionLogBehavior<SetAssetTagFormatCommand, SystemConfigResult>(actionLog, ctx);
        var cmd = new SetAssetTagFormatCommand("NO-SEQ-TOKEN", ActorId);

        var result = await behavior.Handle(cmd, ct => handler.Handle(cmd, ct), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Format phải chứa token {SEQ:n} (VD {SEQ:3}).", result.Message);
        Assert.Empty(await ctx.SystemSettings.ToListAsync());
        Assert.Empty(await ctx.ActionLogs.ToListAsync());
    }

    [Fact]
    public async Task SetAssetTagFormat_Empty_Rejected()
    {
        await using var ctx = CreateContext(nameof(SetAssetTagFormat_Empty_Rejected));
        var handler = FormatHandler(ctx);

        var result = await handler.Handle(new SetAssetTagFormatCommand("   ", ActorId), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Format không được để trống.", result.Message);
    }

    [Fact]
    public async Task SetAssetTagFormat_Valid_CreatesSetting_AndLogsOnce()
    {
        await using var ctx = CreateContext(nameof(SetAssetTagFormat_Valid_CreatesSetting_AndLogsOnce));
        var handler = FormatHandler(ctx);
        var actionLog = TestHelpers.CreateActionLogService(ctx, ActorId);
        var behavior = new ActionLogBehavior<SetAssetTagFormatCommand, SystemConfigResult>(actionLog, ctx);

        var cmd = new SetAssetTagFormatCommand("AST-{COMPANY}-{YYYY}-{SEQ:4}", ActorId);
        var result = await behavior.Handle(cmd, ct => handler.Handle(cmd, ct), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.Changed);

        var setting = await ctx.SystemSettings.SingleAsync();
        Assert.Equal(AssetTagGenerator.FormatSettingKey, setting.Key);
        Assert.Equal("AST-{COMPANY}-{YYYY}-{SEQ:4}", setting.Value);
        Assert.Equal(ActorId, setting.UpdatedBy);
        Assert.False(string.IsNullOrWhiteSpace(setting.Description));

        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.SystemSetting);
        Assert.Equal(ActionType.Update, log.ActionType);
        Assert.Equal(setting.Id, log.ItemId);
        Assert.Equal(ActorId, log.CreatedBy);
        Assert.Null(log.CompanyId); // global config — intentionally not company-scoped
        Assert.Contains("SEQ:4", log.Note);
        Assert.Contains("format", log.LogMeta); // changes meta preserved
    }

    [Fact]
    public async Task SetAssetTagFormat_Unchanged_NoOp_NoWrite_NoLog()
    {
        await using var ctx = CreateContext(nameof(SetAssetTagFormat_Unchanged_NoOp_NoWrite_NoLog));
        ctx.SystemSettings.Add(new SystemSetting
        {
            Key = AssetTagGenerator.FormatSettingKey,
            Value = "AST-{COMPANY}-{YYYY}-{SEQ:3}"
        });
        await ctx.SaveChangesAsync();
        var handler = FormatHandler(ctx);
        var actionLog = TestHelpers.CreateActionLogService(ctx, ActorId);
        var behavior = new ActionLogBehavior<SetAssetTagFormatCommand, SystemConfigResult>(actionLog, ctx);

        var cmd = new SetAssetTagFormatCommand("AST-{COMPANY}-{YYYY}-{SEQ:3}", ActorId);
        var result = await behavior.Handle(cmd, ct => handler.Handle(cmd, ct), CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.Changed);
        Assert.Equal("Đã lưu cấu hình.", result.Message);
        Assert.Empty(await ctx.ActionLogs.ToListAsync()); // no-op must not spam audit rows
    }

    [Fact]
    public async Task SetAssetTagFormat_UpdatesExisting_AndLogsOldToNew()
    {
        await using var ctx = CreateContext(nameof(SetAssetTagFormat_UpdatesExisting_AndLogsOldToNew));
        ctx.SystemSettings.Add(new SystemSetting { Key = AssetTagGenerator.FormatSettingKey, Value = "OLD-{SEQ:1}" });
        await ctx.SaveChangesAsync();
        var handler = FormatHandler(ctx);
        var actionLog = TestHelpers.CreateActionLogService(ctx, ActorId);
        var behavior = new ActionLogBehavior<SetAssetTagFormatCommand, SystemConfigResult>(actionLog, ctx);

        var cmd = new SetAssetTagFormatCommand("NEW-{SEQ:2}", ActorId);
        var result = await behavior.Handle(cmd, ct => handler.Handle(cmd, ct), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("NEW-{SEQ:2}", (await ctx.SystemSettings.SingleAsync()).Value);
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.SystemSetting);
        Assert.Contains("OLD-{SEQ:1}", log.Note);
        Assert.Contains("NEW-{SEQ:2}", log.Note);
    }

    [Fact]
    public async Task GetAssetTagFormat_ReturnsDefault_WhenNoSetting()
    {
        await using var ctx = CreateContext(nameof(GetAssetTagFormat_ReturnsDefault_WhenNoSetting));
        var handler = new GetAssetTagFormatQueryHandler(new AssetTagGenerator(ctx));

        var format = await handler.Handle(new GetAssetTagFormatQuery(), CancellationToken.None);

        Assert.Equal(AssetTagGenerator.DefaultFormat, format);
    }

    [Fact]
    public async Task GetAssetTagFormat_ReturnsStoredValue()
    {
        await using var ctx = CreateContext(nameof(GetAssetTagFormat_ReturnsStoredValue));
        ctx.SystemSettings.Add(new SystemSetting { Key = AssetTagGenerator.FormatSettingKey, Value = "CUSTOM-{SEQ:5}" });
        await ctx.SaveChangesAsync();
        var handler = new GetAssetTagFormatQueryHandler(new AssetTagGenerator(ctx));

        var format = await handler.Handle(new GetAssetTagFormatQuery(), CancellationToken.None);

        Assert.Equal("CUSTOM-{SEQ:5}", format);
    }

    // ==================== passkeys-enabled ====================

    [Fact]
    public async Task SetPasskeysEnabled_True_CreatesSetting_AndLogsOnce()
    {
        await using var ctx = CreateContext(nameof(SetPasskeysEnabled_True_CreatesSetting_AndLogsOnce));
        var handler = PasskeysHandler(ctx);
        var actionLog = TestHelpers.CreateActionLogService(ctx, ActorId);
        var behavior = new ActionLogBehavior<SetPasskeysEnabledCommand, SystemConfigResult>(actionLog, ctx);

        var cmd = new SetPasskeysEnabledCommand(true, ActorId);
        var result = await behavior.Handle(cmd, ct => handler.Handle(cmd, ct), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.Changed);
        var setting = await ctx.SystemSettings.SingleAsync();
        Assert.Equal(IWebAuthnService.PasskeysEnabledSettingKey, setting.Key);
        Assert.Equal("true", setting.Value);

        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.SystemSetting);
        Assert.Equal(setting.Id, log.ItemId);
        Assert.Contains("false → true", log.Note);
    }

    [Fact]
    public async Task SetPasskeysEnabled_Unchanged_NoOp_NoLog()
    {
        await using var ctx = CreateContext(nameof(SetPasskeysEnabled_Unchanged_NoOp_NoLog));
        ctx.SystemSettings.Add(new SystemSetting
        {
            Key = IWebAuthnService.PasskeysEnabledSettingKey,
            Value = "true"
        });
        await ctx.SaveChangesAsync();
        var handler = PasskeysHandler(ctx);
        var actionLog = TestHelpers.CreateActionLogService(ctx, ActorId);
        var behavior = new ActionLogBehavior<SetPasskeysEnabledCommand, SystemConfigResult>(actionLog, ctx);

        var cmd = new SetPasskeysEnabledCommand(true, ActorId);
        var result = await behavior.Handle(cmd, ct => handler.Handle(cmd, ct), CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.Changed);
        Assert.Empty(await ctx.ActionLogs.ToListAsync());
    }

    [Fact]
    public async Task GetPasskeysEnabled_DefaultsFalse_AndReadsStoredTrue()
    {
        await using var ctx = CreateContext(nameof(GetPasskeysEnabled_DefaultsFalse_AndReadsStoredTrue));
        var handler = new GetPasskeysEnabledQueryHandler(ctx);

        Assert.False(await handler.Handle(new GetPasskeysEnabledQuery(), CancellationToken.None));

        ctx.SystemSettings.Add(new SystemSetting
        {
            Key = IWebAuthnService.PasskeysEnabledSettingKey,
            Value = "TRUE" // case-insensitive on read
        });
        await ctx.SaveChangesAsync();

        Assert.True(await handler.Handle(new GetPasskeysEnabledQuery(), CancellationToken.None));
    }
}
