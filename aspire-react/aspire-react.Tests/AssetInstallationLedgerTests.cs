using aspire_react.Server.Application.Assets;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [LL-2] Sổ lý lịch lắp đặt — helper dùng chung cho checkout/checkin/archive:
/// <list type="bullet">
///   <item><c>Open</c> — mở khoảng mới (denormalize tên vị trí/hệ thống, người ghi, Source).</item>
///   <item><c>CloseOpenAsync</c> — đóng dòng đang hiệu lực (<c>EndedAt IS NULL AND SupersededAt IS NULL</c>);
///     bảng chỉ ghi thêm nên <c>EndedAt</c> chỉ được đặt khi đang null.</item>
///   <item><c>IsConsistentAsync</c> — bất biến <c>SystemPositionId != null ⟺ đúng 1 dòng mở</c>.</item>
/// </list>
/// (Handler checkout/checkin dùng <c>FromSqlRaw ... FOR UPDATE</c> nên không chạy được trên InMemory —
/// phần ghi thật qua API được kiểm chứng trên Postgres thật; ở đây khóa chặt logic + bất biến.)
/// </summary>
public class AssetInstallationLedgerTests
{
    private static readonly Guid ActorId = Guid.NewGuid();

    private static async Task<(Guid assetId, SystemPosition position)> SeedAsync(AppDbContext db, bool mounted, string suffix)
    {
        var sys = new SystemInfo { Code = $"LL2-2026-{suffix}1", Name = $"Hệ thống {suffix}" };
        var pos = new SystemPosition { Code = $"LL2-2026-{suffix}2", Name = $"Vị trí {suffix}", SystemInfoId = sys.Id };
        db.SystemInfos.Add(sys);
        db.SystemPositions.Add(pos);

        var asset = new Asset
        {
            AssetTag = $"LL2-{suffix}",
            Name = $"Máy {suffix}",
            IsConfirmed = true,
            SystemPositionId = mounted ? pos.Id : null
        };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();

        return (asset.Id, pos);
    }

    // ==================== Open ====================

    [Fact]
    public async Task Open_CreatesOpenRow_WithDenormalizedNames_ActorAndApiSource()
    {
        await using var db = TestHelpers.CreateContext(nameof(Open_CreatesOpenRow_WithDenormalizedNames_ActorAndApiSource));
        var (assetId, pos) = await SeedAsync(db, mounted: true, "1");
        var startedAt = new DateTime(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc);

        var row = InstallationLedger.Open(db, assetId, pos, "Hệ thống 1", startedAt,
            ActorId, "Nguyễn Văn A", AssetInstallationSource.Api);
        await db.SaveChangesAsync();

        var saved = await db.AssetInstallations.SingleAsync(i => i.Id == row.Id);
        Assert.Equal(assetId, saved.AssetId);
        Assert.Equal(pos.Id, saved.SystemPositionId);
        Assert.Equal(pos.Name, saved.SystemPositionName);      // denormalize tên vị trí
        Assert.Equal(pos.SystemInfoId, saved.SystemInfoId);
        Assert.Equal("Hệ thống 1", saved.SystemInfoName);      // denormalize tên hệ thống
        Assert.Equal(startedAt, saved.StartedAt);
        Assert.Null(saved.EndedAt);                            // khoảng MỞ
        Assert.Null(saved.SupersededAt);
        Assert.Equal(ActorId, saved.LoggedById);
        Assert.Equal("Nguyễn Văn A", saved.LoggedByName);
        Assert.Equal(AssetInstallationSource.Api, saved.Source);
    }

    // ==================== CloseOpen ====================

    [Fact]
    public async Task CloseOpen_SetsEndedAt_WhenTheRowIsOpen()
    {
        await using var db = TestHelpers.CreateContext(nameof(CloseOpen_SetsEndedAt_WhenTheRowIsOpen));
        var (assetId, pos) = await SeedAsync(db, mounted: true, "2");
        var startedAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        var endedAt = new DateTime(2026, 10, 17, 9, 30, 0, DateTimeKind.Utc);
        InstallationLedger.Open(db, assetId, pos, "Hệ thống 2", startedAt, null, null, AssetInstallationSource.Api);
        await db.SaveChangesAsync();

        var closed = await InstallationLedger.CloseOpenAsync(db, assetId, endedAt, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(1, closed);
        var row = await db.AssetInstallations.SingleAsync(i => i.AssetId == assetId);
        Assert.Equal(endedAt, row.EndedAt);
    }

    [Fact]
    public async Task CloseOpen_DoesNotOverwrite_ExistingEndedAt()
    {
        await using var db = TestHelpers.CreateContext(nameof(CloseOpen_DoesNotOverwrite_ExistingEndedAt));
        var (assetId, pos) = await SeedAsync(db, mounted: true, "3");
        var firstEnd = new DateTime(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc);
        InstallationLedger.Open(db, assetId, pos, null, new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
            null, null, AssetInstallationSource.Api);
        await db.SaveChangesAsync();

        await InstallationLedger.CloseOpenAsync(db, assetId, firstEnd, CancellationToken.None);
        await db.SaveChangesAsync();

        // Lần đóng thứ hai (bug/retry) KHÔNG được ghi đè mốc đã chốt — bảng chỉ ghi thêm.
        var closedAgain = await InstallationLedger.CloseOpenAsync(db, assetId, firstEnd.AddDays(3), CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(0, closedAgain);
        Assert.Equal(firstEnd, (await db.AssetInstallations.SingleAsync(i => i.AssetId == assetId)).EndedAt);
    }

    [Fact]
    public async Task CloseOpen_IgnoresSupersededOpenRow()
    {
        await using var db = TestHelpers.CreateContext(nameof(CloseOpen_IgnoresSupersededOpenRow));
        var (assetId, pos) = await SeedAsync(db, mounted: false, "4");

        // Dòng bị ĐÍNH CHÍNH: EndedAt vẫn null nhưng SupersededAt đã có → KHÔNG phải dòng đang hiệu lực.
        var superseded = new AssetInstallation
        {
            AssetId = assetId,
            SystemPositionId = pos.Id,
            StartedAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
            SupersededAt = new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc)
        };
        db.AssetInstallations.Add(superseded);
        await db.SaveChangesAsync();

        var closed = await InstallationLedger.CloseOpenAsync(db, assetId, new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc), CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(0, closed);
        Assert.Null((await db.AssetInstallations.SingleAsync(i => i.Id == superseded.Id)).EndedAt);
    }

    // ==================== Bất biến ====================

    [Fact]
    public async Task IsConsistent_True_WhenMountedWithExactlyOneOpenRow()
    {
        await using var db = TestHelpers.CreateContext(nameof(IsConsistent_True_WhenMountedWithExactlyOneOpenRow));
        var (assetId, pos) = await SeedAsync(db, mounted: true, "5");
        InstallationLedger.Open(db, assetId, pos, null, DateTime.UtcNow, null, null, AssetInstallationSource.Api);
        await db.SaveChangesAsync();

        var asset = await db.Assets.SingleAsync(a => a.Id == assetId);
        Assert.True(await InstallationLedger.IsConsistentAsync(db, asset, CancellationToken.None));
    }

    [Fact]
    public async Task IsConsistent_False_WhenMountedWithoutOpenRow()
    {
        await using var db = TestHelpers.CreateContext(nameof(IsConsistent_False_WhenMountedWithoutOpenRow));
        var (assetId, _) = await SeedAsync(db, mounted: true, "6");

        var asset = await db.Assets.SingleAsync(a => a.Id == assetId);
        Assert.False(await InstallationLedger.IsConsistentAsync(db, asset, CancellationToken.None));
    }

    [Fact]
    public async Task IsConsistent_False_WhenMountedWithTwoOpenRows()
    {
        await using var db = TestHelpers.CreateContext(nameof(IsConsistent_False_WhenMountedWithTwoOpenRows));
        var (assetId, pos) = await SeedAsync(db, mounted: true, "7");
        InstallationLedger.Open(db, assetId, pos, null, DateTime.UtcNow.AddDays(-2), null, null, AssetInstallationSource.Api);
        InstallationLedger.Open(db, assetId, pos, null, DateTime.UtcNow.AddDays(-1), null, null, AssetInstallationSource.Api);
        await db.SaveChangesAsync();

        var asset = await db.Assets.SingleAsync(a => a.Id == assetId);
        Assert.False(await InstallationLedger.IsConsistentAsync(db, asset, CancellationToken.None));
    }

    [Fact]
    public async Task IsConsistent_False_WhenNotMountedButOpenRowExists()
    {
        await using var db = TestHelpers.CreateContext(nameof(IsConsistent_False_WhenNotMountedButOpenRowExists));
        var (assetId, pos) = await SeedAsync(db, mounted: false, "8");
        InstallationLedger.Open(db, assetId, pos, null, DateTime.UtcNow, null, null, AssetInstallationSource.Api);
        await db.SaveChangesAsync();

        var asset = await db.Assets.SingleAsync(a => a.Id == assetId);
        Assert.False(await InstallationLedger.IsConsistentAsync(db, asset, CancellationToken.None));
    }

    [Fact]
    public async Task IsConsistent_False_WhenOpenRowPointsToAnotherPosition()
    {
        await using var db = TestHelpers.CreateContext(nameof(IsConsistent_False_WhenOpenRowPointsToAnotherPosition));
        var (assetId, pos) = await SeedAsync(db, mounted: true, "9");
        var otherPos = new SystemPosition { Code = "LL2-2026-991", Name = "Vị trí khác", SystemInfoId = pos.SystemInfoId };
        db.SystemPositions.Add(otherPos);
        await db.SaveChangesAsync();

        InstallationLedger.Open(db, assetId, otherPos, null, DateTime.UtcNow, null, null, AssetInstallationSource.Api);
        await db.SaveChangesAsync();

        var asset = await db.Assets.SingleAsync(a => a.Id == assetId); // trỏ tới pos, dòng mở lại là otherPos
        Assert.False(await InstallationLedger.IsConsistentAsync(db, asset, CancellationToken.None));
    }

    [Fact]
    public async Task IsConsistent_IgnoresSupersededOpenRow()
    {
        await using var db = TestHelpers.CreateContext(nameof(IsConsistent_IgnoresSupersededOpenRow));
        var (assetId, pos) = await SeedAsync(db, mounted: true, "10");
        var correct = InstallationLedger.Open(db, assetId, pos, null, DateTime.UtcNow, null, null, AssetInstallationSource.Api);
        // Bản bị đính chính: cùng asset, EndedAt null nhưng đã SupersededAt → không tính vào bất biến.
        db.AssetInstallations.Add(new AssetInstallation
        {
            AssetId = assetId,
            SystemPositionId = pos.Id,
            StartedAt = DateTime.UtcNow.AddDays(-5),
            SupersededAt = DateTime.UtcNow.AddDays(-4),
            CorrectsEntryId = correct.Id
        });
        await db.SaveChangesAsync();

        var asset = await db.Assets.SingleAsync(a => a.Id == assetId);
        Assert.True(await InstallationLedger.IsConsistentAsync(db, asset, CancellationToken.None));
    }

    // ==================== Tên người ghi ====================

    [Fact]
    public async Task ResolveActorName_PrefersFullName_ThenUsername_NullWhenMissing()
    {
        await using var db = TestHelpers.CreateContext(nameof(ResolveActorName_PrefersFullName_ThenUsername_NullWhenMissing));
        var named = new User { Username = "no-name", FirstName = "Trần", LastName = "Thị B", Email = "b@test.local", IsActive = true };
        var unnamed = new User { Username = "chi-co-username", FirstName = "", LastName = "", Email = "c@test.local", IsActive = true };
        db.Users.AddRange(named, unnamed);
        await db.SaveChangesAsync();

        Assert.Equal("Trần Thị B", await InstallationLedger.ResolveActorNameAsync(db, named.Id, CancellationToken.None));
        Assert.Equal("chi-co-username", await InstallationLedger.ResolveActorNameAsync(db, unnamed.Id, CancellationToken.None));
        Assert.Null(await InstallationLedger.ResolveActorNameAsync(db, null, CancellationToken.None));
        Assert.Null(await InstallationLedger.ResolveActorNameAsync(db, Guid.NewGuid(), CancellationToken.None));
    }

    // ==================== [LL-2b] Pre-check TRƯỚC khi thay đổi ====================
    // Handler checkout/checkin/archive gọi ValidateBeforeChangeAsync() sau khi đã khoá hàng asset
    // (FOR UPDATE) và TRƯỚC mọi thay đổi change tracker. Pre-check KHÔNG tự sửa dữ liệu để che lệch.

    [Fact]
    public async Task ValidateBeforeChange_Ok_WhenMountedWithOneOpenRow()
    {
        await using var db = TestHelpers.CreateContext(nameof(ValidateBeforeChange_Ok_WhenMountedWithOneOpenRow));
        var (assetId, pos) = await SeedAsync(db, mounted: true, "20");
        var startedAt = new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc);
        InstallationLedger.Open(db, assetId, pos, null, startedAt, null, null, AssetInstallationSource.Api);
        await db.SaveChangesAsync();

        var asset = await db.Assets.SingleAsync(a => a.Id == assetId);
        Assert.Null(await InstallationLedger.ValidateBeforeChangeAsync(db, asset, null, CancellationToken.None));
        // closingAt = StartedAt đúng biên của CHECK (EndedAt >= StartedAt) → vẫn hợp lệ.
        Assert.Null(await InstallationLedger.ValidateBeforeChangeAsync(db, asset, startedAt, CancellationToken.None));
        Assert.Null(await InstallationLedger.ValidateBeforeChangeAsync(db, asset, startedAt.AddMinutes(30), CancellationToken.None));
    }

    [Fact]
    public async Task ValidateBeforeChange_Ok_WhenNotMountedAndNoRows()
    {
        await using var db = TestHelpers.CreateContext(nameof(ValidateBeforeChange_Ok_WhenNotMountedAndNoRows));
        var (assetId, _) = await SeedAsync(db, mounted: false, "21");

        var asset = await db.Assets.SingleAsync(a => a.Id == assetId);
        Assert.Null(await InstallationLedger.ValidateBeforeChangeAsync(db, asset, null, CancellationToken.None));
        Assert.Null(await InstallationLedger.ValidateBeforeChangeAsync(db, asset, DateTime.UtcNow, CancellationToken.None));
    }

    [Theory]
    [InlineData(true)]  // checkout (không đóng khoảng) → closingAt = null
    [InlineData(false)] // checkin/archive (đóng khoảng)
    public async Task ValidateBeforeChange_Inconsistent_WhenMountedWithoutOpenRow(bool closingAtNull)
    {
        await using var db = TestHelpers.CreateContext($"{nameof(ValidateBeforeChange_Inconsistent_WhenMountedWithoutOpenRow)}_{closingAtNull}");
        var (assetId, _) = await SeedAsync(db, mounted: true, closingAtNull ? "22" : "23");

        var asset = await db.Assets.SingleAsync(a => a.Id == assetId);
        var error = await InstallationLedger.ValidateBeforeChangeAsync(db, asset,
            closingAtNull ? null : DateTime.UtcNow, CancellationToken.None);
        Assert.Equal(InstallationLedger.Inconsistent, error);
    }

    /// <summary>Kịch bản LL-2b #5: <c>SystemPositionId NULL</c> nhưng CÒN dòng mở → checkout tới User
    /// (pre-check với closingAt = null) phải bị chặn bằng INCONSISTENT, không tự đóng dòng nào.</summary>
    [Fact]
    public async Task ValidateBeforeChange_Inconsistent_WhenNotMountedButOpenRowExists()
    {
        await using var db = TestHelpers.CreateContext(nameof(ValidateBeforeChange_Inconsistent_WhenNotMountedButOpenRowExists));
        var (assetId, pos) = await SeedAsync(db, mounted: false, "24");
        InstallationLedger.Open(db, assetId, pos, null, DateTime.UtcNow.AddDays(-3), null, null, AssetInstallationSource.Api);
        await db.SaveChangesAsync();

        var asset = await db.Assets.SingleAsync(a => a.Id == assetId); // SystemPositionId = null, 1 dòng mở
        var error = await InstallationLedger.ValidateBeforeChangeAsync(db, asset, null, CancellationToken.None);

        Assert.Equal(InstallationLedger.Inconsistent, error);
        // KHÔNG có dòng nào bị sửa: pre-check chỉ đọc.
        Assert.Null((await db.AssetInstallations.SingleAsync(i => i.AssetId == assetId)).EndedAt);
    }

    [Fact]
    public async Task ValidateBeforeChange_Inconsistent_WhenOpenRowPointsToAnotherPosition()
    {
        await using var db = TestHelpers.CreateContext(nameof(ValidateBeforeChange_Inconsistent_WhenOpenRowPointsToAnotherPosition));
        var (assetId, pos) = await SeedAsync(db, mounted: true, "25");
        var otherPos = new SystemPosition { Code = "LL2-2026-951", Name = "Vị trí khác", SystemInfoId = pos.SystemInfoId };
        db.SystemPositions.Add(otherPos);
        await db.SaveChangesAsync();
        InstallationLedger.Open(db, assetId, otherPos, null, DateTime.UtcNow, null, null, AssetInstallationSource.Api);
        await db.SaveChangesAsync();

        var asset = await db.Assets.SingleAsync(a => a.Id == assetId);
        Assert.Equal(InstallationLedger.Inconsistent,
            await InstallationLedger.ValidateBeforeChangeAsync(db, asset, DateTime.UtcNow, CancellationToken.None));
    }

    [Fact]
    public async Task ValidateBeforeChange_EndBeforeStart_WhenClosingBeforeOpenStartedAt()
    {
        await using var db = TestHelpers.CreateContext(nameof(ValidateBeforeChange_EndBeforeStart_WhenClosingBeforeOpenStartedAt));
        var (assetId, pos) = await SeedAsync(db, mounted: true, "26");
        var startedAt = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
        InstallationLedger.Open(db, assetId, pos, null, startedAt, null, null, AssetInstallationSource.Api);
        await db.SaveChangesAsync();

        var asset = await db.Assets.SingleAsync(a => a.Id == assetId);
        Assert.Equal(InstallationLedger.EndBeforeStart,
            await InstallationLedger.ValidateBeforeChangeAsync(db, asset, startedAt.AddSeconds(-1), CancellationToken.None));
        // Vẫn là bất biến đúng (chỉ mốc thời gian sai) — không bị báo nhầm là INCONSISTENT.
        Assert.NotEqual(InstallationLedger.Inconsistent,
            await InstallationLedger.ValidateBeforeChangeAsync(db, asset, startedAt.AddSeconds(-1), CancellationToken.None));
    }

    [Fact]
    public async Task ValidateBeforeChange_IgnoresSupersededOpenRow()
    {
        await using var db = TestHelpers.CreateContext(nameof(ValidateBeforeChange_IgnoresSupersededOpenRow));
        var (assetId, pos) = await SeedAsync(db, mounted: true, "27");
        var correct = InstallationLedger.Open(db, assetId, pos, null, DateTime.UtcNow, null, null, AssetInstallationSource.Api);
        db.AssetInstallations.Add(new AssetInstallation
        {
            AssetId = assetId,
            SystemPositionId = pos.Id,
            StartedAt = DateTime.UtcNow.AddDays(-9),
            SupersededAt = DateTime.UtcNow.AddDays(-8),
            CorrectsEntryId = correct.Id
        });
        await db.SaveChangesAsync();

        var asset = await db.Assets.SingleAsync(a => a.Id == assetId);
        Assert.Null(await InstallationLedger.ValidateBeforeChangeAsync(db, asset, DateTime.UtcNow.AddDays(1), CancellationToken.None));
    }

    [Fact]
    public void DescribeError_MapsLedgerCodes()
    {
        Assert.Contains("không nhất quán", InstallationLedger.DescribeError(InstallationLedger.Inconsistent));
        Assert.Contains("trước mốc bắt đầu", InstallationLedger.DescribeError(InstallationLedger.EndBeforeStart));
        Assert.NotEqual(InstallationLedger.DescribeError(InstallationLedger.Inconsistent),
            InstallationLedger.DescribeError(InstallationLedger.EndBeforeStart));
    }
}
