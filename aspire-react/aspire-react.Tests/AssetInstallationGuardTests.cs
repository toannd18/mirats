using aspire_react.Server.Application.Assets.Commands;
using aspire_react.Server.Application.SystemInfos.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [LL-1] Guard xóa liên quan lý lịch lắp đặt (asset_installations) — KHÔNG để lộ 500 FK thô:
/// <list type="bullet">
///   <item><c>POSITION_IN_USE_BY_ASSET</c> — xóa SystemPosition đang có asset gắn.</item>
///   <item><c>SYSTEM_IN_USE_BY_ASSET</c> — xóa SystemInfo có bất kỳ vị trí nào đang có asset gắn.</item>
///   <item><c>ASSET_HAS_INSTALLATION_HISTORY</c> — xóa Asset đã có dòng lý lịch lắp đặt.</item>
/// </list>
/// Mỗi guard có một ca BỊ CHẶN (400 + error_code) và một ca ĐƯỢC PHÉP (xóa thành công).
/// Fixture dựng qua entity InMemory (không dùng SQL); asset seed <c>IsConfirmed=false</c> để vượt
/// guard đầu tiên của DeleteAssetCommand.
/// </summary>
public class AssetInstallationGuardTests
{
    private static Guid ActorId { get; } = Guid.NewGuid();

    private sealed record Fixture(
        Guid SystemId,        // hệ thống có vị trí đang gắn asset
        Guid PositionBusyId,  // vị trí đang gắn asset
        Guid PositionFreeId,  // vị trí trống (cùng hệ thống)
        Guid SystemFreeId,    // hệ thống không có asset nào
        Guid PositionFreeSystemId,
        Guid AssetMountedId,  // asset đang gắn + có lý lịch lắp đặt
        Guid AssetHistoryId,  // asset KHÔNG gắn nhưng có lý lịch lắp đặt (khoảng đã đóng)
        Guid AssetCleanId);   // asset chưa có lý lịch lắp đặt

    private static async Task<Fixture> SeedAsync(AppDbContext db, string suffix)
    {
        var sys = new SystemInfo { Code = $"LLS-2026-{suffix}1", Name = "Hệ thống bị khóa" };
        var posBusy = new SystemPosition { Code = $"LLS-2026-{suffix}2", Name = "Vị trí đang gắn thiết bị", SystemInfoId = sys.Id };
        var posFree = new SystemPosition { Code = $"LLS-2026-{suffix}3", Name = "Vị trí trống", SystemInfoId = sys.Id };

        var sysFree = new SystemInfo { Code = $"LLS-2026-{suffix}4", Name = "Hệ thống tự do" };
        var posFreeSys = new SystemPosition { Code = $"LLS-2026-{suffix}5", Name = "Vị trí hệ thống tự do", SystemInfoId = sysFree.Id };

        db.SystemInfos.AddRange(sys, sysFree);
        db.SystemPositions.AddRange(posBusy, posFree, posFreeSys);

        // IsConfirmed=false: chưa confirm → vượt guard đầu của DeleteAssetCommand, chạm được guard lý lịch.
        var assetMounted = new Asset { AssetTag = $"LL-{suffix}-M", Name = "Máy đang gắn", IsConfirmed = false, SystemPositionId = posBusy.Id };
        var assetHistory = new Asset { AssetTag = $"LL-{suffix}-H", Name = "Máy có lý lịch đã đóng", IsConfirmed = false };
        var assetClean = new Asset { AssetTag = $"LL-{suffix}-C", Name = "Máy chưa có lý lịch", IsConfirmed = false };
        db.Assets.AddRange(assetMounted, assetHistory, assetClean);
        await db.SaveChangesAsync();

        // Lý lịch lắp đặt: assetMounted đang mở (EndedAt=null); assetHistory đã đóng (khoảng cũ).
        db.AssetInstallations.AddRange(
            new AssetInstallation
            {
                AssetId = assetMounted.Id,
                SystemPositionId = posBusy.Id,
                SystemPositionName = posBusy.Name,
                SystemInfoId = sys.Id,
                SystemInfoName = sys.Name,
                StartedAt = DateTime.UtcNow.AddDays(-10)
            },
            new AssetInstallation
            {
                AssetId = assetHistory.Id,
                SystemPositionId = posFree.Id,
                SystemPositionName = posFree.Name,
                SystemInfoId = sys.Id,
                SystemInfoName = sys.Name,
                StartedAt = DateTime.UtcNow.AddDays(-30),
                EndedAt = DateTime.UtcNow.AddDays(-20)
            });
        await db.SaveChangesAsync();

        return new Fixture(sys.Id, posBusy.Id, posFree.Id, sysFree.Id, posFreeSys.Id,
            assetMounted.Id, assetHistory.Id, assetClean.Id);
    }

    private static DeleteSystemPositionCommandHandler DeletePosHandler(AppDbContext db)
        => new(db, new TestHelpers.SuperUserScope());

    private static DeleteSystemInfoCommandHandler DeleteSysHandler(AppDbContext db)
        => new(db, new TestHelpers.SuperUserScope());

    private static DeleteAssetCommandHandler DeleteAssetHandler(AppDbContext db)
        => new(db, TestHelpers.CreateActionLogService(db, ActorId), new TestHelpers.SuperUserScope());

    // ==================== POSITION_IN_USE_BY_ASSET ====================

    [Fact]
    public async Task DeletePosition_WithMountedAsset_BlockedWithClear400()
    {
        await using var db = TestHelpers.CreateContext(nameof(DeletePosition_WithMountedAsset_BlockedWithClear400));
        var f = await SeedAsync(db, "1");

        var result = await DeletePosHandler(db)
            .Handle(new DeleteSystemPositionCommand(f.SystemId, f.PositionBusyId, ActorId), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("POSITION_IN_USE_BY_ASSET", result.ErrorCode);
        Assert.True(await db.SystemPositions.AnyAsync(p => p.Id == f.PositionBusyId));
    }

    [Fact]
    public async Task DeletePosition_WithoutMountedAsset_Allowed()
    {
        await using var db = TestHelpers.CreateContext(nameof(DeletePosition_WithoutMountedAsset_Allowed));
        var f = await SeedAsync(db, "2");

        var result = await DeletePosHandler(db)
            .Handle(new DeleteSystemPositionCommand(f.SystemId, f.PositionFreeId, ActorId), CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(await db.SystemPositions.AnyAsync(p => p.Id == f.PositionFreeId));
    }

    // ==================== SYSTEM_IN_USE_BY_ASSET ====================

    [Fact]
    public async Task DeleteSystem_WithMountedAssetOnAnyPosition_BlockedWithClear400()
    {
        await using var db = TestHelpers.CreateContext(nameof(DeleteSystem_WithMountedAssetOnAnyPosition_BlockedWithClear400));
        var f = await SeedAsync(db, "3");

        var result = await DeleteSysHandler(db)
            .Handle(new DeleteSystemInfoCommand(f.SystemId, ActorId), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("SYSTEM_IN_USE_BY_ASSET", result.ErrorCode);
        Assert.True(await db.SystemInfos.AnyAsync(s => s.Id == f.SystemId));
        Assert.True(await db.SystemPositions.AnyAsync(p => p.Id == f.PositionBusyId)); // không cascade
    }

    [Fact]
    public async Task DeleteSystem_WithoutMountedAsset_Allowed()
    {
        await using var db = TestHelpers.CreateContext(nameof(DeleteSystem_WithoutMountedAsset_Allowed));
        var f = await SeedAsync(db, "4");

        var result = await DeleteSysHandler(db)
            .Handle(new DeleteSystemInfoCommand(f.SystemFreeId, ActorId), CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(await db.SystemInfos.AnyAsync(s => s.Id == f.SystemFreeId));
        Assert.False(await db.SystemPositions.AnyAsync(p => p.Id == f.PositionFreeSystemId)); // cascade vị trí
    }

    // ==================== ASSET_HAS_INSTALLATION_HISTORY ====================

    [Fact]
    public async Task DeleteAsset_WithInstallationHistory_BlockedWithClear400()
    {
        await using var db = TestHelpers.CreateContext(nameof(DeleteAsset_WithInstallationHistory_BlockedWithClear400));
        var f = await SeedAsync(db, "5");

        var result = await DeleteAssetHandler(db)
            .Handle(new DeleteAssetCommand { AssetId = f.AssetHistoryId, CurrentUserId = ActorId }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ASSET_HAS_INSTALLATION_HISTORY", result.ErrorCode);
        Assert.True(await db.Assets.AnyAsync(a => a.Id == f.AssetHistoryId));
    }

    [Fact]
    public async Task DeleteAsset_WithoutInstallationHistory_Allowed()
    {
        await using var db = TestHelpers.CreateContext(nameof(DeleteAsset_WithoutInstallationHistory_Allowed));
        var f = await SeedAsync(db, "6");

        var result = await DeleteAssetHandler(db)
            .Handle(new DeleteAssetCommand { AssetId = f.AssetCleanId, CurrentUserId = ActorId }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(await db.Assets.AnyAsync(a => a.Id == f.AssetCleanId));
    }

    [Fact]
    public async Task DeleteAsset_ConfirmedWithInstallationHistory_ConfirmedGuardRunsFirst()
    {
        await using var db = TestHelpers.CreateContext(nameof(DeleteAsset_ConfirmedWithInstallationHistory_ConfirmedGuardRunsFirst));
        var f = await SeedAsync(db, "7");

        // Đổi asset đang gắn thành đã confirm → thứ tự guard IsConfirmed TRƯỚC InstallationHistory.
        var asset = await db.Assets.SingleAsync(a => a.Id == f.AssetMountedId);
        asset.IsConfirmed = true;
        await db.SaveChangesAsync();

        var result = await DeleteAssetHandler(db)
            .Handle(new DeleteAssetCommand { AssetId = f.AssetMountedId, CurrentUserId = ActorId }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ASSET_CONFIRMED_CANNOT_DELETE", result.ErrorCode);
    }
}
