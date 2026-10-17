using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Assets.Commands;

public record ArchiveAssetCommand(Guid AssetId, Guid LocationId, Guid CurrentUserId, string? Note = null) : IRequest<AssetResult>;

public class ArchiveAssetCommandHandler : IRequestHandler<ArchiveAssetCommand, AssetResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IActionLogService _actionLogService;

    public ArchiveAssetCommandHandler(IApplicationDbContext context, IActionLogService actionLogService)
    {
        _context = context;
        _actionLogService = actionLogService;
    }

    public async Task<AssetResult> Handle(ArchiveAssetCommand request, CancellationToken cancellationToken)
    {
        // [LL-2] Archive phải ghi lý lịch (đóng khoảng đang mở) → cần transaction + khóa hàng FOR UPDATE
        // trên asset, cùng pattern concurrency của Checkout/Checkin (Task O-FIX). Trước LL-2 handler này
        // không có transaction/khóa: đóng khoảng trong cùng transaction là điều kiện để rollback toàn bộ
        // khi bất biến lý lịch bị phá.
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            var asset = await _context.Assets
                .FromSqlRaw("SELECT * FROM assets WHERE \"Id\" = {0} FOR UPDATE", request.AssetId)
                .FirstOrDefaultAsync(cancellationToken);
            if (asset == null)
                return new AssetResult(false, "Asset not found.", ErrorCode: "NOT_FOUND");

            if (asset.Status == AssetStatus.Archived)
                return new AssetResult(false, "Asset is already archived.", ErrorCode: "ALREADY_ARCHIVED");

            var oldStatus = asset.Status;
            var oldLocationId = asset.LocationId;

            var endedAt = DateTime.UtcNow;

            // [LL-2b] PRE-CHECK trước khi thay đổi bất cứ thứ gì: (1) bất biến lý lịch của trạng thái HIỆN
            // TẠI phải đúng (không tự đóng dòng để che dữ liệu lệch), (2) mốc kết thúc không được trước
            // StartedAt của dòng đang mở (VD checkout được đặt ở tương lai). Vi phạm → 400, không ghi gì.
            var preError = await InstallationLedger.ValidateBeforeChangeAsync(_context, asset, endedAt, cancellationToken);
            if (preError != null)
                return new AssetResult(false, InstallationLedger.DescribeError(preError), ErrorCode: preError);

            // Archive (Lưu trữ / thanh lý) is an explicit, terminal action — it is NOT triggered
            // by a check-in. LocationId is required (storage/disposal location).
            asset.Status = AssetStatus.Archived;
            asset.LocationId = request.LocationId;
            asset.CurrentAssignmentId = null;
            asset.SystemPositionId = null;

            // [LL-2] Thiết bị đang gắn vị trí → ĐÓNG dòng lý lịch đang mở (chỉ khi EndedAt IS NULL).
            await InstallationLedger.CloseOpenAsync(_context, asset.Id, endedAt, cancellationToken);

            _actionLogService.LogAction(
                itemType: ItemType.Asset,
                itemId: asset.Id,
                actionType: ActionType.Archive,
                loggedByUserId: request.CurrentUserId,
                locationId: request.LocationId,
                companyId: asset.CompanyId,
                note: request.Note ?? "Asset archived",
                logMeta: JsonSerializer.Serialize(new
                {
                    changes = new Dictionary<string, object?>
                    {
                        ["status"] = new { old = oldStatus.ToString(), @new = AssetStatus.Archived.ToString() },
                        ["locationId"] = new { old = oldLocationId?.ToString(), @new = request.LocationId.ToString() }
                    }
                }));

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
                return new AssetResult(false, InstallationLedger.DescribeError(InstallationLedger.EndBeforeStart),
                    ErrorCode: InstallationLedger.EndBeforeStart);
            }

            // [LL-2] Bất biến: archived ⇒ SystemPositionId == null ⇒ không còn dòng mở.
            if (!await InstallationLedger.IsConsistentAsync(_context, asset, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new AssetResult(false, InstallationLedger.DescribeError(InstallationLedger.Inconsistent),
                    ErrorCode: InstallationLedger.Inconsistent);
            }

            await transaction.CommitAsync(cancellationToken);
            return new AssetResult(true, "Asset archived successfully.", AssetId: asset.Id);
        });
    }
}