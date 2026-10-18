using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Application.Common.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Assets.Commands;

/// <summary>
/// [LL-5] Đính chính MỘT dòng lý lịch lắp đặt (<c>asset_installations</c>).
/// <para>
/// Quy trình (append-only, 1 transaction): khoá asset <c>FOR UPDATE</c> → pre-check bất biến →
/// đánh dấu bản cũ <c>SupersededAt</c> → INSERT bản mới (<c>CorrectsEntryId</c> = bản cũ,
/// <c>Source = Correction</c>) → ActionLog → <c>SaveChanges</c> → post-check bất biến → commit.
/// Bản cũ KHÔNG bị sửa/xoá (giữ nguyên mọi cột nghiệp vụ, chỉ thêm <c>SupersededAt</c>).
/// </para>
/// <para>
/// Luật (chốt 2026-10-18):
/// <list type="bullet">
///   <item>Mọi trường trong payload là OPTIONAL trừ <see cref="Reason"/>; không có thay đổi ⇒ 400.</item>
///   <item>Dòng ĐANG MỞ chỉ sửa được <c>startedAt</c> + <c>note</c> (gửi <c>endedAt</c> ⇒ 400, gửi
///     <c>systemPositionId</c> ⇒ 400) — vì dòng đang mở phải tiếp tục trỏ đúng vị trí hiện tại của asset.</item>
///   <item>Dòng ĐÃ ĐÓNG sửa được <c>startedAt</c>, <c>endedAt</c>, vị trí, ghi chú.</item>
///   <item>KHÔNG đính chính nào được đổi <c>asset.SystemPositionId</c>.</item>
///   <item>Không đính chính được bản đã bị thay thế (<c>SupersededAt != null</c>) ⇒ 409.</item>
/// </list>
/// </para>
/// </summary>
public record CorrectInstallationCommand(
    Guid AssetId,
    Guid EntryId,
    DateTime? StartedAt,
    DateTime? EndedAt,
    Guid? SystemPositionId,
    string? Note,
    string? Reason,
    Guid CurrentUserId) : IRequest<CorrectionResult>;

/// <summary>Kết quả đính chính. <c>Success = false</c> ⇒ xem <see cref="ErrorCode"/>.</summary>
public record CorrectionResult(
    bool Success,
    string Message,
    Guid? NewEntryId = null,
    Guid? SupersededEntryId = null,
    string? ErrorCode = null);

public class CorrectInstallationCommandValidator : AbstractValidator<CorrectInstallationCommand>
{
    /// <summary>Lý do ngắn nhất được chấp nhận — sửa lịch sử mà lý do qua loa thì không kiểm toán được.</summary>
    public const int MinReasonLength = 10;

    /// <summary>Trần độ dài lý do (khớp cột <c>Note</c>/ActionLog Note sẵn có).</summary>
    public const int MaxReasonLength = 500;

    public CorrectInstallationCommandValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Lý do đính chính là bắt buộc.").WithErrorCode("CORRECTION_REASON_REQUIRED")
            .MinimumLength(MinReasonLength).WithMessage($"Lý do đính chính phải có ít nhất {MinReasonLength} ký tự.")
            .WithErrorCode("CORRECTION_REASON_REQUIRED")
            .MaximumLength(MaxReasonLength).WithMessage($"Lý do đính chính không quá {MaxReasonLength} ký tự.")
            .WithErrorCode("CORRECTION_REASON_REQUIRED");

        RuleFor(x => x.StartedAt)
            .Must(v => !AssetTimestampRules.IsBeyondFutureWindow(v, DateTime.UtcNow))
            .WithMessage(AssetTimestampRules.Message)
            .WithErrorCode(AssetTimestampRules.Code);

        RuleFor(x => x.EndedAt)
            .Must(v => !AssetTimestampRules.IsBeyondFutureWindow(v, DateTime.UtcNow))
            .WithMessage(AssetTimestampRules.Message)
            .WithErrorCode(AssetTimestampRules.Code);
    }
}

public class CorrectInstallationCommandHandler : IRequestHandler<CorrectInstallationCommand, CorrectionResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IActionLogService _actionLogService;
    private readonly ICompanyScopeService _companyScope;

    public CorrectInstallationCommandHandler(
        IApplicationDbContext context,
        IActionLogService actionLogService,
        ICompanyScopeService companyScope)
    {
        _context = context;
        _actionLogService = actionLogService;
        _companyScope = companyScope;
    }

    public async Task<CorrectionResult> Handle(CorrectInstallationCommand request, CancellationToken cancellationToken)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            var asset = await _context.Assets
                .FromSqlRaw("SELECT * FROM assets WHERE \"Id\" = {0} FOR UPDATE", request.AssetId)
                .FirstOrDefaultAsync(cancellationToken);

            if (asset == null)
                return new CorrectionResult(false, "Asset not found.", ErrorCode: "NOT_FOUND");

            // Company-scoping (mirror Update/DeleteAssetCommand): asset ngoài công ty coi như KHÔNG tồn tại.
            var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
            if (userCompanyId.HasValue && asset.CompanyId.HasValue && asset.CompanyId.Value != userCompanyId.Value)
                return new CorrectionResult(false, "Asset not found.", ErrorCode: "NOT_FOUND");

            var entry = await _context.AssetInstallations
                .FirstOrDefaultAsync(i => i.Id == request.EntryId && i.AssetId == asset.Id, cancellationToken);

            if (entry == null)
                return new CorrectionResult(false, "Không tìm thấy bản ghi lý lịch lắp đặt.",
                    ErrorCode: "INSTALLATION_ENTRY_NOT_FOUND");

            if (entry.SupersededAt != null)
                return new CorrectionResult(false, "Bản ghi này đã bị đính chính trước đó — hãy đính chính bản đang hiệu lực.",
                    ErrorCode: InstallationLedger.AlreadySuperseded);

            var isOpen = entry.EndedAt == null;

            // Dòng ĐANG MỞ: chỉ startedAt + note. Không đặt mốc kết thúc (đó là Thu hồi) và KHÔNG đổi vị trí
            // (bất biến: dòng mở phải trỏ đúng vị trí hiện tại của asset ⇒ đổi vị trí phải qua Thu hồi + Cấp phát lại).
            if (isOpen && request.EndedAt.HasValue)
                return new CorrectionResult(false,
                    "Dòng đang mở không thể đặt mốc kết thúc — dùng chức năng Thu hồi/Lưu trữ để đóng dòng.",
                    ErrorCode: "INSTALLATION_CLOSE_USE_CHECKIN");

            if (isOpen && request.SystemPositionId.HasValue)
                return new CorrectionResult(false,
                    "Dòng đang mở không thể đổi vị trí — hãy Thu hồi rồi Cấp phát lại vào vị trí đúng.",
                    ErrorCode: "INSTALLATION_OPEN_POSITION_LOCKED");

            // Giá trị hiệu dụng sau patch (chỉ trường được gửi mới thay đổi).
            var newStartedAt = request.StartedAt ?? entry.StartedAt;
            var newEndedAt = isOpen ? (DateTime?)null : (request.EndedAt ?? entry.EndedAt);
            var newNote = request.Note ?? entry.Note;
            var newPositionId = isOpen ? entry.SystemPositionId : (request.SystemPositionId ?? entry.SystemPositionId);

            var changes = new Dictionary<string, object?>();
            if (newStartedAt != entry.StartedAt)
                changes["started_at"] = new { old = entry.StartedAt, @new = newStartedAt };
            if (!isOpen && newEndedAt != entry.EndedAt)
                changes["ended_at"] = new { old = entry.EndedAt, @new = newEndedAt };
            if (newPositionId != entry.SystemPositionId)
                changes["system_position_id"] = new { old = entry.SystemPositionId?.ToString(), @new = newPositionId?.ToString() };
            if (!string.Equals(newNote, entry.Note, StringComparison.Ordinal))
                changes["note"] = new { old = entry.Note, @new = newNote };

            if (changes.Count == 0)
                return new CorrectionResult(false, "Không có thay đổi nào so với bản ghi hiện tại.",
                    ErrorCode: "CORRECTION_NO_CHANGES");

            if (newEndedAt.HasValue && newEndedAt.Value < newStartedAt)
                return new CorrectionResult(false, InstallationLedger.DescribeError(InstallationLedger.EndBeforeStart),
                    ErrorCode: InstallationLedger.EndBeforeStart);

            // Vị trí mới (nếu đổi) phải tồn tại; giữ snapshot tên vị trí/hệ thống cho bản mới.
            SystemPosition? position = null;
            string? positionSystemInfoName = null;
            if (newPositionId != entry.SystemPositionId && newPositionId.HasValue)
            {
                position = await _context.SystemPositions.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == newPositionId.Value, cancellationToken);

                if (position == null)
                    return new CorrectionResult(false, "Vị trí không tồn tại hoặc đã bị xoá.",
                        ErrorCode: "POSITION_NOT_FOUND");

                positionSystemInfoName = await _context.SystemInfos.AsNoTracking()
                    .Where(s => s.Id == position.SystemInfoId)
                    .Select(s => s.Name)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            // Pre-check bất biến TRƯỚC khi thay đổi change tracker (mirror checkout/checkin/archive).
            var preError = await InstallationLedger.ValidateBeforeChangeAsync(_context, asset, null, cancellationToken);
            if (preError != null)
                return new CorrectionResult(false, InstallationLedger.DescribeError(preError), ErrorCode: preError);

            var supersededAt = DateTime.UtcNow;
            entry.SupersededAt = supersededAt;

            var actorName = await InstallationLedger.ResolveActorNameAsync(_context, request.CurrentUserId, cancellationToken);
            var newEntry = new AssetInstallation
            {
                AssetId = asset.Id,
                SystemPositionId = newPositionId,
                SystemPositionName = position?.Name ?? entry.SystemPositionName,
                SystemInfoId = position?.SystemInfoId ?? entry.SystemInfoId,
                SystemInfoName = positionSystemInfoName ?? entry.SystemInfoName,
                StartedAt = newStartedAt,
                EndedAt = newEndedAt,
                Note = newNote,
                LoggedById = request.CurrentUserId,
                LoggedByName = actorName,
                Source = AssetInstallationSource.Correction,
                CorrectsEntryId = entry.Id
            };
            _context.AssetInstallations.Add(newEntry);

            _actionLogService.LogAction(
                itemType: ItemType.Asset,
                itemId: asset.Id,
                actionType: ActionType.Correct,
                loggedByUserId: request.CurrentUserId,
                locationId: asset.LocationId,
                companyId: asset.CompanyId,
                note: request.Reason,
                logMeta: JsonSerializer.Serialize(new
                {
                    reason = request.Reason,
                    superseded_entry_id = entry.Id,
                    corrects_entry_id = entry.Id,
                    new_entry_id = newEntry.Id,
                    changes
                }));

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pg
                && pg.SqlState == Npgsql.PostgresErrorCodes.ExclusionViolation)
            {
                // EX_asset_installations_no_overlap (23P01): khoảng mới chồng khoảng ĐANG hiệu lực khác của cùng asset
                // (bản vừa bị supersede không tính) → 409, không lọt 500.
                await transaction.RollbackAsync(cancellationToken);
                return new CorrectionResult(false,
                    "Khoảng lắp đặt bị chồng với lý lịch đã có của thiết bị — kiểm tra lại mốc thời gian.",
                    ErrorCode: InstallationLedger.Overlap);
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pg
                && pg.SqlState == Npgsql.PostgresErrorCodes.CheckViolation)
            {
                // Lưới an toàn cho CHECK CK_asset_installations_ended_after_started (23514).
                await transaction.RollbackAsync(cancellationToken);
                return new CorrectionResult(false, InstallationLedger.DescribeError(InstallationLedger.EndBeforeStart),
                    ErrorCode: InstallationLedger.EndBeforeStart);
            }

            // Post-check bất biến sau SaveChanges, TRƯỚC commit: sai ⇒ rollback, không ghi gì.
            if (!await InstallationLedger.IsConsistentAsync(_context, asset, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CorrectionResult(false, InstallationLedger.DescribeError(InstallationLedger.Inconsistent),
                    ErrorCode: InstallationLedger.Inconsistent);
            }

            await transaction.CommitAsync(cancellationToken);

            return new CorrectionResult(true, "Đã đính chính lý lịch lắp đặt.", newEntry.Id, entry.Id);
        });
    }
}
