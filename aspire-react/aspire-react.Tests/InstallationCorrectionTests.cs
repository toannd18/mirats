using aspire_react.Server.Application.Assets;
using aspire_react.Server.Application.Assets.Commands;
using aspire_react.Server.Application.Assets.Queries;
using aspire_react.Server.Domain.Authorization;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [LL-5] Đính chính lý lịch lắp đặt (<c>asset_installations</c>).
/// <para>
/// <b>Vì sao toàn bộ ca handler chạy trên Postgres THẬT:</b> handler khoá hàng bằng
/// <c>FromSqlRaw("... FOR UPDATE")</c> — InMemory không dịch được (đúng tiền lệ AssetTests: checkout/checkin
/// chỉ được kiểm qua validator). Ngoài ra 2 hành vi CHỈ Postgres mới có: exclusion constraint
/// <c>EX_asset_installations_no_overlap</c> (23P01) và CHECK <c>CK_asset_installations_ended_after_started</c> (23514).
/// </para>
/// <para>
/// Các test cần DB dùng <see cref="ThrowawayDbFact"/> (cờ <c>MIRATS_TEST_THROWAWAY_DB=1</c>) + connection string
/// DB TẠM trong <see cref="ConnectionStringEnv"/>. DB tạm PHẢI nằm trong container Postgres RIÊNG (workflow §3.16).
/// </para>
/// </summary>
public class InstallationCorrectionTests
{
    /// <summary>Connection string tới database TẠM (container Postgres riêng) dùng cho các ca handler.</summary>
    public const string ConnectionStringEnv = "MIRATS_TEST_POSTGRES";

    private const string ValidReason = "Ghi nhầm mốc thời gian khi lắp đặt";

    // ==================== Hợp đồng: enum / permission / validator (không cần DB) ====================

    [Fact]
    public void Contract_AppendedEnumValues_AndDedicatedPermission()
    {
        // Chỉ THÊM giá trị (append-only) — con trỏ/dữ liệu cũ không đổi nghĩa.
        Assert.Equal(23, (int)ActionType.Correct);
        Assert.Equal(4, (int)AssetInstallationSource.Correction);

        var permission = PermissionCatalog.All.Single(p => p.Code == "assets.correct");
        Assert.Equal("assets", permission.Resource);
        Assert.Equal("correct", permission.Action);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ngắn quá")]
    public void Validator_RejectsMissingOrTooShortReason(string? reason)
    {
        var result = new CorrectInstallationCommandValidator().Validate(Command(reason: reason));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == "CORRECTION_REASON_REQUIRED");
    }

    [Fact]
    public void Validator_RejectsFutureTimestamps_AndAcceptsValidPayload()
    {
        var validator = new CorrectInstallationCommandValidator();
        var future = DateTime.UtcNow.AddMinutes(30);

        var started = validator.Validate(Command(startedAt: future));
        Assert.False(started.IsValid);
        Assert.Contains(started.Errors, e => e.ErrorCode == AssetTimestampRules.Code);

        var ended = validator.Validate(Command(endedAt: future));
        Assert.False(ended.IsValid);

        Assert.True(validator.Validate(Command()).IsValid);
    }

    // ==================== Handler trên Postgres THẬT ====================

    /// <summary>
    /// (a)+(b) Dòng ĐÃ ĐÓNG: sửa mốc thời gian + vị trí ⇒ bản cũ chỉ được thêm <c>SupersededAt</c> (không sửa cột
    /// nghiệp vụ), bản mới có <c>CorrectsEntryId</c>/<c>Source=Correction</c>/người đính chính, ActionLog <c>Correct</c>
    /// có reason + changes; <b>asset.SystemPositionId KHÔNG đổi</b> (quyết định: không đính chính nào đổi vị trí hiện tại).
    /// </summary>
    [ThrowawayDbFact]
    public async Task Correction_ClosedEntry_ChangesTimesAndPosition_OnRealPostgres()
    {
        await using var db = await CreateContextAsync();
        var fx = await SeedAsync(db);

        var newStart = fx.Base.AddDays(-3);
        var newEnd = fx.Base.AddDays(-2).AddHours(5);

        var result = await Handler(db, fx.Actor.Id).Handle(
            new CorrectInstallationCommand(fx.Asset.Id, fx.Closed.Id, newStart, newEnd, fx.PosB.Id,
                "Ghi chú mới", "Ghi nhầm vị trí và mốc thời gian", fx.Actor.Id), CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.NewEntryId);
        Assert.Equal(fx.Closed.Id, result.SupersededEntryId);

        var oldRow = await db.AssetInstallations.AsNoTracking().SingleAsync(i => i.Id == fx.Closed.Id);
        Assert.NotNull(oldRow.SupersededAt);
        Assert.Equal(fx.Closed.StartedAt, oldRow.StartedAt);        // bản cũ KHÔNG bị sửa dữ liệu nghiệp vụ
        Assert.Equal(fx.Closed.EndedAt, oldRow.EndedAt);
        Assert.Equal(fx.Closed.SystemPositionId, oldRow.SystemPositionId);
        Assert.Equal(fx.Closed.Note, oldRow.Note);

        var newRow = await db.AssetInstallations.AsNoTracking().SingleAsync(i => i.Id == result.NewEntryId);
        Assert.Equal(fx.Closed.Id, newRow.CorrectsEntryId);
        Assert.Equal(AssetInstallationSource.Correction, newRow.Source);
        Assert.Equal(newStart, newRow.StartedAt);
        Assert.Equal(newEnd, newRow.EndedAt);
        Assert.Equal(fx.PosB.Id, newRow.SystemPositionId);
        Assert.Equal(fx.PosB.Name, newRow.SystemPositionName);      // denormalize theo vị trí mới
        Assert.Equal(fx.System.Name, newRow.SystemInfoName);
        Assert.Equal("Ghi chú mới", newRow.Note);
        Assert.Equal(fx.Actor.Id, newRow.LoggedById);
        Assert.Null(newRow.SupersededAt);

        var asset = await db.Assets.AsNoTracking().SingleAsync(a => a.Id == fx.Asset.Id);
        Assert.Equal(fx.PosA.Id, asset.SystemPositionId);           // KHÔNG đổi vị trí hiện tại của thiết bị

        var log = await db.ActionLogs.AsNoTracking().SingleAsync(l =>
            l.ItemType == ItemType.Asset && l.ItemId == fx.Asset.Id && l.ActionType == ActionType.Correct);
        Assert.Equal("Ghi nhầm vị trí và mốc thời gian", log.Note);
        Assert.NotNull(log.LogMeta);
        Assert.Contains("reason", log.LogMeta);
        Assert.Contains("started_at", log.LogMeta);
        Assert.Contains("system_position_id", log.LogMeta);
    }

    /// <summary>
    /// (k)+(i) Dòng ĐANG MỞ: chỉ <c>startedAt</c> (+note); gửi <c>systemPositionId</c> ⇒ 400
    /// <c>INSTALLATION_OPEN_POSITION_LOCKED</c>; gửi <c>endedAt</c> ⇒ 400 <c>INSTALLATION_CLOSE_USE_CHECKIN</c>.
    /// Sau khi sửa mốc, bất biến "đúng 1 dòng mở trỏ đúng vị trí asset" vẫn đúng.
    /// </summary>
    [ThrowawayDbFact]
    public async Task Correction_OpenEntry_GuardsAndStartOnly_OnRealPostgres()
    {
        await using var db = await CreateContextAsync();
        var fx = await SeedAsync(db);

        var locked = await Handler(db, fx.Actor.Id).Handle(
            new CorrectInstallationCommand(fx.Asset.Id, fx.Open.Id, null, null, fx.PosB.Id, null, ValidReason, fx.Actor.Id),
            CancellationToken.None);
        Assert.False(locked.Success);
        Assert.Equal("INSTALLATION_OPEN_POSITION_LOCKED", locked.ErrorCode);

        var closeViaCorrection = await Handler(db, fx.Actor.Id).Handle(
            new CorrectInstallationCommand(fx.Asset.Id, fx.Open.Id, null, fx.Base.AddHours(1), null, null, ValidReason, fx.Actor.Id),
            CancellationToken.None);
        Assert.False(closeViaCorrection.Success);
        Assert.Equal("INSTALLATION_CLOSE_USE_CHECKIN", closeViaCorrection.ErrorCode);

        // Hợp lệ: chỉ sửa mốc bắt đầu (lùi 1 giờ — vẫn nằm sau mốc kết thúc của dòng backfill đã đóng [Base-1d, Base)).
        var newStart = fx.Base.AddHours(1);
        var ok = await Handler(db, fx.Actor.Id).Handle(
            new CorrectInstallationCommand(fx.Asset.Id, fx.Open.Id, newStart, null, null, null, ValidReason, fx.Actor.Id),
            CancellationToken.None);
        Assert.True(ok.Success, ok.Message);

        var newRow = await db.AssetInstallations.AsNoTracking().SingleAsync(i => i.Id == ok.NewEntryId);
        Assert.Equal(newStart, newRow.StartedAt);
        Assert.Null(newRow.EndedAt);                                 // vẫn là dòng ĐANG MỞ
        Assert.Equal(fx.PosA.Id, newRow.SystemPositionId);           // giữ nguyên vị trí
        Assert.Equal(AssetInstallationSource.Correction, newRow.Source);

        var asset = await db.Assets.AsNoTracking().SingleAsync(a => a.Id == fx.Asset.Id);
        Assert.Equal(fx.PosA.Id, asset.SystemPositionId);

        var openRows = await db.AssetInstallations.AsNoTracking()
            .Where(i => i.AssetId == fx.Asset.Id && i.EndedAt == null && i.SupersededAt == null)
            .ToListAsync();
        Assert.Single(openRows);                                     // bất biến: đúng 1 dòng mở
        Assert.Equal(fx.PosA.Id, openRows[0].SystemPositionId);       // ...và trỏ đúng vị trí hiện tại
    }

    /// <summary>
    /// (c)+(j)+(e)+(l)+(m) Các mã lỗi đầu vào: <c>EndBeforeStart</c>, <c>CORRECTION_NO_CHANGES</c>,
    /// <c>INSTALLATION_ALREADY_SUPERSEDED</c>, <c>INSTALLATION_ENTRY_NOT_FOUND</c> và company-scope (hide-existence).
    /// Mọi ca đều KHÔNG ghi gì.
    /// </summary>
    [ThrowawayDbFact]
    public async Task Correction_ErrorCodes_AndNothingWritten_OnRealPostgres()
    {
        await using var db = await CreateContextAsync();
        var fx = await SeedAsync(db);
        var handler = Handler(db, fx.Actor.Id);

        // (c) mốc kết thúc trước mốc bắt đầu (dòng đã đóng).
        var endBeforeStart = await handler.Handle(
            new CorrectInstallationCommand(fx.Asset.Id, fx.Closed.Id, null, fx.Closed.StartedAt.AddDays(-1),
                null, null, ValidReason, fx.Actor.Id), CancellationToken.None);
        Assert.False(endBeforeStart.Success);
        Assert.Equal(InstallationLedger.EndBeforeStart, endBeforeStart.ErrorCode);

        // (j) không có thay đổi nào (chỉ gửi reason).
        var noChanges = await handler.Handle(
            new CorrectInstallationCommand(fx.Asset.Id, fx.Closed.Id, null, null, null, null, ValidReason, fx.Actor.Id),
            CancellationToken.None);
        Assert.False(noChanges.Success);
        Assert.Equal("CORRECTION_NO_CHANGES", noChanges.ErrorCode);

        // (e) bản đã bị đính chính trước đó.
        var already = await handler.Handle(
            new CorrectInstallationCommand(fx.Asset.Id, fx.Superseded.Id, null, null, null, "đổi ghi chú", ValidReason, fx.Actor.Id),
            CancellationToken.None);
        Assert.False(already.Success);
        Assert.Equal("INSTALLATION_ALREADY_SUPERSEDED", already.ErrorCode);

        // (l) entryId không thuộc asset / không tồn tại.
        var notFound = await handler.Handle(
            new CorrectInstallationCommand(fx.Asset.Id, Guid.NewGuid(), null, null, null, "đổi ghi chú", ValidReason, fx.Actor.Id),
            CancellationToken.None);
        Assert.False(notFound.Success);
        Assert.Equal("INSTALLATION_ENTRY_NOT_FOUND", notFound.ErrorCode);

        // (m) company-scope: user thuộc công ty KHÁC ⇒ coi như không tồn tại (mirror Update/DeleteAssetCommand).
        var scoped = new CorrectInstallationCommandHandler(
            db,
            TestHelpers.CreateActionLogService(db, fx.Actor.Id),
            new TestHelpers.FakeScope { Super = false, CompanyId = Guid.NewGuid() });
        var hidden = await scoped.Handle(
            new CorrectInstallationCommand(fx.Asset.Id, fx.Closed.Id, null, null, null, "đổi ghi chú", ValidReason, fx.Actor.Id),
            CancellationToken.None);
        Assert.False(hidden.Success);
        Assert.Equal("NOT_FOUND", hidden.ErrorCode);

        // Không ca nào ghi gì: không có bản đính chính mới, và chỉ dòng seed sẵn mới có SupersededAt.
        Assert.Empty(await db.AssetInstallations.AsNoTracking()
            .Where(i => i.AssetId == fx.Asset.Id && i.CorrectsEntryId != null)
            .ToListAsync());
        Assert.Equal(1, await db.AssetInstallations.AsNoTracking()
            .CountAsync(i => i.AssetId == fx.Asset.Id && i.SupersededAt != null));
    }

    /// <summary>
    /// (g)+(h) Đính chính dòng BACKFILL: bản mới KHÔNG còn nhãn ước tính (<c>Source=Correction</c>),
    /// timeline chỉ còn bản mới (<c>isCorrection = true</c>, bản cũ biến mất) và sự kiện <c>CORRECT</c> có details là lý do
    /// nhưng KHÔNG thuộc nhóm "Sự kiện chính".
    /// </summary>
    [ThrowawayDbFact]
    public async Task Correction_BackfillLosesEstimated_Timeline_OnRealPostgres()
    {
        await using var db = await CreateContextAsync();
        var fx = await SeedAsync(db);
        var timeline = new AssetTimelineQueryHandler(db, new TestHelpers.SuperUserScope());

        var before = await timeline.Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50), CancellationToken.None);
        var backfillBefore = Assert.Single(before.Events!, e => e.EventType == "INSTALL" && e.RefId == fx.Backfill.Id);
        Assert.True(backfillBefore.IsEstimated);
        Assert.False(backfillBefore.IsCorrection);
        Assert.DoesNotContain("Backfill LL-2", backfillBefore.Details);   // [LL-4b] không lộ Note kỹ thuật

        var reason = "Mốc ước tính sai — đã xác nhận lại theo biên bản";
        var result = await Handler(db, fx.Actor.Id).Handle(
            new CorrectInstallationCommand(fx.Asset.Id, fx.Backfill.Id, fx.Base.AddDays(-1), fx.Base.AddHours(-1),
                null, null, reason, fx.Actor.Id), CancellationToken.None);
        Assert.True(result.Success, result.Message);

        var newRow = await db.AssetInstallations.AsNoTracking().SingleAsync(i => i.Id == result.NewEntryId);
        Assert.Equal(AssetInstallationSource.Correction, newRow.Source);

        var after = await timeline.Handle(new AssetTimelineQuery(fx.Asset.Id, null, 50), CancellationToken.None);
        var events = after.Events!;
        Assert.DoesNotContain(events, e => e.RefId == fx.Backfill.Id);     // bản cũ bị ẩn hoàn toàn

        var corrected = Assert.Single(events, e => e.EventType == "INSTALL" && e.RefId == result.NewEntryId);
        Assert.True(corrected.IsCorrection);
        Assert.False(corrected.IsEstimated);                              // mất nhãn ước tính

        var correctEvent = Assert.Single(events, e => e.EventType == "CORRECT");
        Assert.Equal("Đính chính lý lịch lắp đặt", correctEvent.Title);
        Assert.Contains("Lý do:", correctEvent.Details!);
        Assert.Contains("Mốc ước tính sai", correctEvent.Details!);

        var mainOnly = await timeline.Handle(
            new AssetTimelineQuery(fx.Asset.Id, null, 50, new[] { "INSTALL", "REMOVE", "MAINTENANCE", "MAINTENANCE_CAMPAIGN" }),
            CancellationToken.None);
        Assert.DoesNotContain(mainOnly.Events!, e => e.EventType == "CORRECT");   // chỉ hiện ở chế độ "Tất cả"
    }

    /// <summary>
    /// [Bước 6] Note của dòng Backfill KHÔNG được kế thừa: đính chính mà KHÔNG gửi <c>note</c> ⇒ bản mới
    /// <c>Note = null</c> (note kỹ thuật chỉ còn ở bản cũ); CÓ gửi <c>note</c> ⇒ dùng note đó. Dòng KHÔNG phải
    /// Backfill giữ nguyên patch-semantics (không gửi ⇒ kế thừa note cũ) — chứng minh ngoại lệ chỉ áp cho Backfill.
    /// </summary>
    [ThrowawayDbFact]
    public async Task Correction_BackfillNote_NotInherited_OnRealPostgres()
    {
        await using var db = await CreateContextAsync();
        var fx = await SeedAsync(db);
        var handler = Handler(db, fx.Actor.Id);

        // (a) Backfill + KHÔNG gửi note (chỉ đổi startedAt) ⇒ bản mới Note = null.
        var noNote = await handler.Handle(
            new CorrectInstallationCommand(fx.Asset.Id, fx.Backfill.Id,
                fx.Backfill.StartedAt.AddHours(2), null, null, null, ValidReason, fx.Actor.Id),
            CancellationToken.None);
        Assert.True(noNote.Success, noNote.Message);

        var newNoNote = await db.AssetInstallations.AsNoTracking().SingleAsync(i => i.Id == noNote.NewEntryId);
        Assert.Null(newNoNote.Note);
        Assert.Equal(AssetInstallationSource.Correction, newNoNote.Source);

        var oldBackfill = await db.AssetInstallations.AsNoTracking().SingleAsync(i => i.Id == fx.Backfill.Id);
        Assert.NotNull(oldBackfill.SupersededAt);                     // bản cũ bị supersede...
        Assert.Equal(fx.Backfill.Note, oldBackfill.Note);             // ...nhưng GIỮ nguyên note kỹ thuật

        // (b) Backfill KHÁC + CÓ gửi note ⇒ bản mới dùng note đó.
        var second = new AssetInstallation
        {
            AssetId = fx.Asset.Id,
            SystemPositionId = fx.PosB.Id,
            SystemPositionName = fx.PosB.Name,
            SystemInfoId = fx.System.Id,
            SystemInfoName = fx.System.Name,
            StartedAt = fx.Base.AddDays(-6),
            EndedAt = fx.Base.AddDays(-5),
            Source = AssetInstallationSource.Backfill,
            Note = "Backfill LL-2: uoc tinh (2).",
            LoggedById = fx.Actor.Id,
            LoggedByName = "Kiểm Thử"
        };
        db.AssetInstallations.Add(second);
        await db.SaveChangesAsync();

        var withNote = await handler.Handle(
            new CorrectInstallationCommand(fx.Asset.Id, second.Id,
                second.StartedAt.AddHours(1), null, null, "Ghi chú do người dùng nhập", ValidReason, fx.Actor.Id),
            CancellationToken.None);
        Assert.True(withNote.Success, withNote.Message);

        var newWithNote = await db.AssetInstallations.AsNoTracking().SingleAsync(i => i.Id == withNote.NewEntryId);
        Assert.Equal("Ghi chú do người dùng nhập", newWithNote.Note);

        // (c) KHÔNG phải Backfill: không gửi note ⇒ VẪN kế thừa note cũ (hành vi không đổi).
        var inherited = await handler.Handle(
            new CorrectInstallationCommand(fx.Asset.Id, fx.Closed.Id,
                fx.Closed.StartedAt.AddHours(1), null, null, null, ValidReason, fx.Actor.Id),
            CancellationToken.None);
        Assert.True(inherited.Success, inherited.Message);

        var newInherited = await db.AssetInstallations.AsNoTracking().SingleAsync(i => i.Id == inherited.NewEntryId);
        Assert.Equal(fx.Closed.Note, newInherited.Note);
    }

    /// <summary>
    /// [Bước 7] Ngoại lệ "CHỈ XÁC NHẬN MỐC" của dòng Backfill: đính chính chỉ có <c>reason</c> (không đổi trường
    /// nào khác) vẫn HỢP LỆ — vì <c>Backfill → Correction</c> tự nó là thay đổi nghiệp vụ (bản mới MẤT nhãn
    /// "Thời điểm ước tính"). Nguồn KHÁC không đổi gì ⇒ 400 <c>CORRECTION_NO_CHANGES</c>. Kèm ca biên: dòng
    /// Backfill có note RỖNG vẫn hợp lệ (quy tắc KHÔNG phụ thuộc nội dung note kỹ thuật).
    /// </summary>
    [ThrowawayDbFact]
    public async Task Correction_BackfillConfirmOnly_IsValid_OtherSourcesRejected_OnRealPostgres()
    {
        await using var db = await CreateContextAsync();
        var fx = await SeedAsync(db);
        var handler = Handler(db, fx.Actor.Id);

        // (a) Backfill (có note kỹ thuật) + CHỈ reason ⇒ hợp lệ; bản mới mất nhãn ước tính + Note = null.
        var confirm = await handler.Handle(
            new CorrectInstallationCommand(fx.Asset.Id, fx.Backfill.Id, null, null, null, null, ValidReason, fx.Actor.Id),
            CancellationToken.None);
        Assert.True(confirm.Success, confirm.Message);

        var confirmed = await db.AssetInstallations.AsNoTracking().SingleAsync(i => i.Id == confirm.NewEntryId);
        Assert.Equal(AssetInstallationSource.Correction, confirmed.Source);   // mất nhãn ước tính
        Assert.Null(confirmed.Note);                                          // note kỹ thuật không kế thừa
        Assert.Equal(fx.Backfill.StartedAt, confirmed.StartedAt);             // KHÔNG đổi mốc nào
        Assert.Equal(fx.Backfill.EndedAt, confirmed.EndedAt);

        // (b) Ca biên: Backfill KHÁC có note = NULL + CHỈ reason ⇒ vẫn hợp lệ (chuyển nguồn tự nó là thay đổi).
        var noNoteBackfill = new AssetInstallation
        {
            AssetId = fx.Asset.Id,
            SystemPositionId = fx.PosB.Id,
            SystemPositionName = fx.PosB.Name,
            SystemInfoId = fx.System.Id,
            SystemInfoName = fx.System.Name,
            StartedAt = fx.Base.AddDays(-6),
            EndedAt = fx.Base.AddDays(-5),
            Source = AssetInstallationSource.Backfill,
            Note = null,
            LoggedById = fx.Actor.Id,
            LoggedByName = "Kiểm Thử"
        };
        db.AssetInstallations.Add(noNoteBackfill);
        await db.SaveChangesAsync();

        var confirmNoNote = await handler.Handle(
            new CorrectInstallationCommand(fx.Asset.Id, noNoteBackfill.Id, null, null, null, null, ValidReason, fx.Actor.Id),
            CancellationToken.None);
        Assert.True(confirmNoNote.Success, confirmNoNote.Message);

        // (c) Nguồn KHÁC (Api) + không đổi gì ⇒ 400 CORRECTION_NO_CHANGES (không tạo bản rác).
        var noChanges = await handler.Handle(
            new CorrectInstallationCommand(fx.Asset.Id, fx.Closed.Id, null, null, null, null, ValidReason, fx.Actor.Id),
            CancellationToken.None);
        Assert.False(noChanges.Success);
        Assert.Equal("CORRECTION_NO_CHANGES", noChanges.ErrorCode);

        // LogMeta của bản đính chính Backfill ghi rõ thay đổi "source" (Backfill → Correction).
        Assert.True(await db.ActionLogs.AsNoTracking().AnyAsync(l =>
            l.ItemType == ItemType.Asset && l.ItemId == fx.Asset.Id && l.ActionType == ActionType.Correct
            && l.LogMeta != null && l.LogMeta.Contains("source")));
    }

    /// <summary>
    /// (n) Chồng khoảng thời gian: exclusion constraint <c>EX_asset_installations_no_overlap</c> (23P01) ⇒
    /// <c>INSTALLATION_PERIOD_OVERLAP</c> (controller map sang 409) và transaction ROLLBACK — không ghi gì.
    /// </summary>
    [ThrowawayDbFact]
    public async Task Correction_Overlap_ReturnsOverlapCode_AndRollsBack_OnRealPostgres()
    {
        await using var db = await CreateContextAsync();
        var fx = await SeedAsync(db);

        // Dòng đã đóng [Base-2d, Base-1d) đính chính thành [Base-2d, Base+1h) ⇒ chồng dòng ĐANG MỞ [Base, ∞).
        var overlapEnd = fx.Base.AddHours(1);
        var result = await Handler(db, fx.Actor.Id).Handle(
            new CorrectInstallationCommand(fx.Asset.Id, fx.Closed.Id, fx.Closed.StartedAt, overlapEnd,
                null, null, ValidReason, fx.Actor.Id), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(InstallationLedger.Overlap, result.ErrorCode);

        var oldRow = await db.AssetInstallations.AsNoTracking().SingleAsync(i => i.Id == fx.Closed.Id);
        Assert.Null(oldRow.SupersededAt);                                  // rollback: bản cũ nguyên vẹn
        Assert.Empty(await db.AssetInstallations.AsNoTracking()
            .Where(i => i.AssetId == fx.Asset.Id && i.CorrectsEntryId != null)
            .ToListAsync());
    }

    /// <summary>
    /// (o) Bất biến bị phá TỪ TRƯỚC (asset trỏ vị trí A nhưng dòng đang mở lại trỏ vị trí B) ⇒ pre-check chặn:
    /// 400 <c>INSTALLATION_LEDGER_INCONSISTENT</c> và KHÔNG ghi gì.
    /// </summary>
    [ThrowawayDbFact]
    public async Task Correction_BrokenInvariant_ReturnsInconsistentCode_AndRollsBack_OnRealPostgres()
    {
        await using var db = await CreateContextAsync();
        var fx = await SeedAsync(db);

        // Phá bất biến bằng dữ liệu: dòng đang mở CHUYỂN sang vị trí B trong khi asset vẫn trỏ vị trí A.
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE asset_installations SET \"SystemPositionId\" = {0} WHERE \"Id\" = {1}",
            fx.PosB.Id, fx.Open.Id);

        var result = await Handler(db, fx.Actor.Id).Handle(
            new CorrectInstallationCommand(fx.Asset.Id, fx.Closed.Id, fx.Closed.StartedAt.AddHours(1),
                null, null, null, ValidReason, fx.Actor.Id), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(InstallationLedger.Inconsistent, result.ErrorCode);

        var oldRow = await db.AssetInstallations.AsNoTracking().SingleAsync(i => i.Id == fx.Closed.Id);
        Assert.Null(oldRow.SupersededAt);
        Assert.Equal(fx.Closed.StartedAt, oldRow.StartedAt);
        Assert.Empty(await db.AssetInstallations.AsNoTracking()
            .Where(i => i.AssetId == fx.Asset.Id && i.CorrectsEntryId != null)
            .ToListAsync());
    }

    /// <summary>
    /// [LL-5 Bước 4] Endpoint đọc MỘT dòng lý lịch (form đính chính cần đủ `startedAt`/`endedAt`/vị trí/`note`/`source`
    /// — DTO timeline không có). Kiểm 3 dòng (đang mở / đã đóng / backfill) + các ca 404: entry không thuộc asset,
    /// asset khác, và NGOÀI phạm vi công ty (hide-existence).
    /// </summary>
    [ThrowawayDbFact]
    public async Task InstallationQuery_ReturnsRow_AndHidesForeignOrOutOfScope_OnRealPostgres()
    {
        await using var db = await CreateContextAsync();
        var fx = await SeedAsync(db);
        var handler = new AssetInstallationQueryHandler(db, new TestHelpers.SuperUserScope());

        var open = await handler.Handle(new AssetInstallationQuery(fx.Asset.Id, fx.Open.Id), CancellationToken.None);
        Assert.True(open.Success);
        var openDto = open.Installation!;
        Assert.Equal(fx.Open.Id, openDto.Id);
        Assert.Equal(fx.Asset.Id, openDto.AssetId);
        Assert.Equal(fx.PosA.Id, openDto.SystemPositionId);
        Assert.Equal(fx.PosA.Name, openDto.SystemPositionName);
        Assert.Equal(fx.System.Name, openDto.SystemInfoName);
        Assert.Equal(fx.Base, openDto.StartedAt);
        Assert.Equal(DateTimeKind.Utc, openDto.StartedAt.Kind);            // JSON có hậu tố Z
        Assert.Null(openDto.EndedAt);                                      // dòng ĐANG MỞ
        Assert.Equal("ghi chú đang mở", openDto.Note);
        Assert.Equal(AssetInstallationSource.Api, openDto.Source);
        Assert.False(openDto.IsEstimated);
        Assert.False(openDto.IsCorrection);
        Assert.Null(openDto.CorrectsEntryId);
        Assert.Null(openDto.SupersededAt);
        Assert.Equal("Kiểm Thử", openDto.LoggedByName);

        var closed = await handler.Handle(new AssetInstallationQuery(fx.Asset.Id, fx.Closed.Id), CancellationToken.None);
        Assert.True(closed.Success);
        Assert.Equal(fx.Closed.EndedAt, closed.Installation!.EndedAt);
        Assert.Equal(DateTimeKind.Utc, closed.Installation!.EndedAt!.Value.Kind);

        var backfill = await handler.Handle(new AssetInstallationQuery(fx.Asset.Id, fx.Backfill.Id), CancellationToken.None);
        Assert.True(backfill.Success);
        Assert.True(backfill.Installation!.IsEstimated);
        Assert.Equal(AssetInstallationSource.Backfill, backfill.Installation!.Source);

        // entry KHÔNG thuộc asset ⇒ 404.
        Assert.False((await handler.Handle(new AssetInstallationQuery(fx.Asset.Id, Guid.NewGuid()), CancellationToken.None)).Success);
        // asset KHÁC ⇒ 404.
        Assert.False((await handler.Handle(new AssetInstallationQuery(Guid.NewGuid(), fx.Open.Id), CancellationToken.None)).Success);
        // NGOÀI company scope ⇒ 404 (hide-existence).
        var scoped = new AssetInstallationQueryHandler(db, new TestHelpers.FakeScope { Super = false, CompanyId = Guid.NewGuid() });
        Assert.False((await scoped.Handle(new AssetInstallationQuery(fx.Asset.Id, fx.Open.Id), CancellationToken.None)).Success);
    }

    // ==================== Hạ tầng test ====================

    private sealed record Fixture(
        DateTime Base,
        Asset Asset,
        SystemInfo System,
        SystemPosition PosA,
        SystemPosition PosB,
        User Actor,
        AssetInstallation Open,
        AssetInstallation Closed,
        AssetInstallation Backfill,
        AssetInstallation Superseded);

    private static CorrectInstallationCommand Command(
        DateTime? startedAt = null, DateTime? endedAt = null, Guid? systemPositionId = null, string? reason = ValidReason)
        => new(Guid.NewGuid(), Guid.NewGuid(), startedAt, endedAt, systemPositionId, null, reason, Guid.NewGuid());

    private static CorrectInstallationCommandHandler Handler(AppDbContext db, Guid actorId)
        => new(db, TestHelpers.CreateActionLogService(db, actorId), new TestHelpers.SuperUserScope());

    private static async Task<AppDbContext> CreateContextAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnv)?.Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                $"Thiếu {ConnectionStringEnv} (connection string tới DB TẠM trong container Postgres RIÊNG). " +
                "VD: Host=127.0.0.1;Port=<host-port>;Database=ll5_test;Username=postgres;Password=<pw>");

        // Migrations ở project aspire-react.Server ⇒ phải chỉ định tường minh (y như PersistenceServiceCollectionExtensions),
        // nếu không MigrateAsync() không áp migration nào.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("aspire-react.Server"))
            .Options;

        var db = new AppDbContext(options, new TestHelpers.SuperUserScope());
        await ThrowawayDbMigrator.MigrateOnceAsync(db, connectionString);
        return db;
    }

    /// <summary>
    /// Dựng 1 asset đang gắn vị trí A (dòng MỞ) + 1 dòng đã ĐÓNG ở vị trí A + 1 dòng BACKFILL đã đóng ở vị trí B
    /// + 1 dòng đã bị superseded. Mọi tên/mã có suffix ngẫu nhiên (chạy nhiều lần trên cùng DB tạm không đụng nhau).
    /// </summary>
    private static async Task<Fixture> SeedAsync(AppDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var company = new Company { Name = $"Công ty LL5 {suffix}", Code = $"LL5{suffix.ToUpperInvariant()}" };
        var actor = new User
        {
            Username = $"qa-ll5-{suffix}",
            Email = $"qa-ll5-{suffix}@test.local",
            FirstName = "Kiểm",
            LastName = "Thử"
        };
        var system = new SystemInfo { Code = $"LL5A-2026-{suffix[..6].ToUpperInvariant()}", Name = $"Hệ thống LL5 {suffix}" };
        var posA = new SystemPosition { Code = $"POSA-2026-{suffix[..6].ToUpperInvariant()}", Name = $"Kệ A {suffix}", SystemInfoId = system.Id };
        var posB = new SystemPosition { Code = $"POSB-2026-{suffix[..6].ToUpperInvariant()}", Name = $"Kệ B {suffix}", SystemInfoId = system.Id };
        var asset = new Asset
        {
            AssetTag = $"LL5-AST-{suffix}",
            Name = $"Thiết bị LL5 {suffix}",
            IsConfirmed = true,
            CompanyId = company.Id,
            SystemPositionId = posA.Id
        };

        var baseTime = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

        AssetInstallation Row(Guid positionId, string positionName, DateTime startedAt, DateTime? endedAt,
            AssetInstallationSource source, string? note, DateTime? supersededAt = null)
            => new()
            {
                AssetId = asset.Id,
                SystemPositionId = positionId,
                SystemPositionName = positionName,
                SystemInfoId = system.Id,
                SystemInfoName = system.Name,
                StartedAt = startedAt,
                EndedAt = endedAt,
                Source = source,
                Note = note,
                LoggedById = actor.Id,
                LoggedByName = "Kiểm Thử",
                SupersededAt = supersededAt
            };

        var open = Row(posA.Id, posA.Name, baseTime, null, AssetInstallationSource.Api, "ghi chú đang mở");
        var closed = Row(posA.Id, posA.Name, baseTime.AddDays(-2), baseTime.AddDays(-1), AssetInstallationSource.Api, "ghi chú đã đóng");
        var backfill = Row(posB.Id, posB.Name, baseTime.AddDays(-1), baseTime, AssetInstallationSource.Backfill,
            "Backfill LL-2: thiết bị đang gắn vị trí nhưng chưa có lý lịch lắp đặt (ƯỚC TÍNH).");
        var superseded = Row(posA.Id, posA.Name, baseTime.AddDays(-4), baseTime.AddDays(-3), AssetInstallationSource.Api,
            "đã bị thay thế", baseTime.AddDays(-1));

        db.Companies.Add(company);
        db.Users.Add(actor);
        db.SystemInfos.Add(system);
        db.SystemPositions.AddRange(posA, posB);
        db.Assets.Add(asset);
        db.AssetInstallations.AddRange(open, closed, backfill, superseded);
        await db.SaveChangesAsync();

        return new Fixture(baseTime, asset, system, posA, posB, actor, open, closed, backfill, superseded);
    }
}
