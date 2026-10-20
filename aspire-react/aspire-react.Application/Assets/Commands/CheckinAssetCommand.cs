using aspire_react.Server.Application.Common.ActionLogs;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Application.Common.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Assets.Commands;

public record CheckinAssetCommand(
    Guid AssetId,
    Guid LocationId,
    string? Note,
    DateTime? CheckinAt,
    Guid CurrentUserId) : IRequest<CheckinResult>;

public record CheckinResult(bool Success, string Message, string? ErrorCode = null);

/// <summary>
/// [Bước 0.2] Validator cho checkin — hiện chỉ chặn mốc thời gian ở tương lai. Các bất biến nghiệp vụ
/// (asset tồn tại / đang cấp phát / lý lịch nhất quán) do handler kiểm tra trong transaction + FOR UPDATE.
/// </summary>
public class CheckinAssetCommandValidator : AbstractValidator<CheckinAssetCommand>
{
    public CheckinAssetCommandValidator()
    {
        RuleFor(x => x.CheckinAt)
            .Must(v => !AssetTimestampRules.IsBeyondFutureWindow(v, DateTime.UtcNow))
            .WithMessage(AssetTimestampRules.Message)
            .WithErrorCode(AssetTimestampRules.Code);

        // [FIX-GUID-EMPTY 2026-10-20] LocationId bắt buộc: Guid.Empty (body {}) trước đây ghi thẳng vào
        // FK assets.LocationId ⇒ Postgres 23503 ⇒ HTTP 500. Nay 400 + error_code LOCATION_REQUIRED.
        RuleFor(x => x.LocationId)
            .NotEmpty().WithMessage("Vị trí thu hồi là bắt buộc.")
            .WithErrorCode("LOCATION_REQUIRED");
    }
}

public class CheckinAssetCommandHandler : IRequestHandler<CheckinAssetCommand, CheckinResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IActionLogService _actionLogService;

    public CheckinAssetCommandHandler(IApplicationDbContext context, IActionLogService actionLogService)
    {
        _context = context;
        _actionLogService = actionLogService;
    }

    public async Task<CheckinResult> Handle(CheckinAssetCommand request, CancellationToken cancellationToken)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            var asset = await _context.Assets
                .FromSqlRaw("SELECT * FROM assets WHERE \"Id\" = {0} FOR UPDATE", request.AssetId)
                .Include(a => a.CurrentAssignment)
                .FirstOrDefaultAsync(cancellationToken);

            if (asset == null)
                return new CheckinResult(false, "Asset not found.", "ASSET_NOT_FOUND");

            if (asset.CurrentAssignmentId == null)
                return new CheckinResult(false, "Asset is not checked out.", "ASSET_NOT_CHECKED_OUT");

            if (asset.Status != AssetStatus.Deployed)
                return new CheckinResult(false, "Asset is not deployed.", "ASSET_NOT_DEPLOYED");

            // [FIX-GUID-EMPTY] FK PHẢI tồn tại: LocationId rỗng/không có thật trước đây ghi thẳng vào
            // assets.LocationId ⇒ Postgres 23503 ⇒ 500. Nay 400 + RESOURCE_NOT_FOUND, kiểm tra SỚM.
            if (!await _context.Locations.AsNoTracking().AnyAsync(l => l.Id == request.LocationId, cancellationToken))
                return new CheckinResult(false, "Vị trí thu hồi không tồn tại.", "RESOURCE_NOT_FOUND");

            var oldAssignment = asset.CurrentAssignment;
            var oldSnapshot = new Dictionary<string, object?>
            {
                ["current_assignment_id"] = asset.CurrentAssignmentId,
                ["location_id"] = asset.LocationId,
                ["system_position_id"] = asset.SystemPositionId,
                ["status"] = asset.Status.ToString(),
                ["checkin_counter"] = asset.CheckinCounter,
                ["previous_target_type"] = oldAssignment?.TargetType.ToString(),
                ["previous_target_id"] = oldAssignment?.TargetId.ToString()
            };
            var changes = new ActionLogChanges()
                .Track("currentAssignmentId", oldSnapshot["current_assignment_id"])
                .Track("status", oldSnapshot["status"])
                .Track("locationId", oldSnapshot["location_id"]?.ToString())
                .Track("systemPositionId", oldSnapshot["system_position_id"]?.ToString());

            // [LL-2] Mốc thời gian checkin dùng CHUNG cho LastCheckin và EndedAt của lý lịch lắp đặt.
            var endedAt = request.CheckinAt ?? DateTime.UtcNow;

            // [LL-2b] PRE-CHECK trước khi thay đổi bất cứ thứ gì: (1) bất biến lý lịch của trạng thái HIỆN
            // TẠI phải đúng (không tự đóng dòng để che dữ liệu lệch), (2) mốc kết thúc không được trước
            // StartedAt của dòng đang mở. Vi phạm → 400, KHÔNG ghi gì (không assignment/ActionLog/lý lịch).
            var preError = await InstallationLedger.ValidateBeforeChangeAsync(_context, asset, endedAt, cancellationToken);
            if (preError != null)
                return new CheckinResult(false, InstallationLedger.DescribeError(preError), preError);

            asset.CurrentAssignmentId = null;
            asset.CheckinCounter++;
            asset.LastCheckin = endedAt;
            asset.Status = AssetStatus.Pending; // check-in returns to Pending — Archived stays false (check-in is NOT archiving)
            asset.SystemPositionId = null;
            asset.LocationId = request.LocationId;

            // [LL-2] Tháo thiết bị khỏi vị trí → ĐÓNG dòng đang mở (chỉ khi EndedAt IS NULL),
            // cùng mốc thời gian với checkin. Không mở dòng mới (checkin không phải lắp đặt).
            await InstallationLedger.CloseOpenAsync(_context, asset.Id, endedAt, cancellationToken);

            _actionLogService.LogAction(
                itemType: ItemType.Asset,
                itemId: request.AssetId,
                actionType: ActionType.Checkin,
                loggedByUserId: request.CurrentUserId,
                targetType: oldAssignment?.TargetType,
                targetId: oldAssignment?.TargetId,
                locationId: request.LocationId,
                companyId: asset.CompanyId,
                note: request.Note,
                logMeta: changes
                    .Set("currentAssignmentId", (string?)null)
                    .Set("status", AssetStatus.Pending.ToString())
                    .Set("locationId", request.LocationId.ToString())
                    .Set("systemPositionId", (string?)null)
                    .ToLogMeta());

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pg
                && pg.SqlState == Npgsql.PostgresErrorCodes.CheckViolation)
            {
                // [LL-2b] Lưới an toàn SQLSTATE 23514 (CHECK "EndedAt" >= "StartedAt"): pre-check đã chặn
                // trường hợp thường gặp; nếu vẫn lọt (VD race) thì trả 400, KHÔNG để lọt 500 thô.
                await transaction.RollbackAsync(cancellationToken);
                return new CheckinResult(false, InstallationLedger.DescribeError(InstallationLedger.EndBeforeStart),
                    InstallationLedger.EndBeforeStart);
            }

            // [LL-2] Bất biến: đã tháo khỏi vị trí → KHÔNG được còn dòng mở nào.
            // Kiểm tra sau SaveChanges, trước Commit → sai thì rollback, không ghi gì.
            if (!await InstallationLedger.IsConsistentAsync(_context, asset, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CheckinResult(false, InstallationLedger.DescribeError(InstallationLedger.Inconsistent),
                    InstallationLedger.Inconsistent);
            }

            await transaction.CommitAsync(cancellationToken);

            return new CheckinResult(true, "Asset checked in successfully.");
        });
    }
}