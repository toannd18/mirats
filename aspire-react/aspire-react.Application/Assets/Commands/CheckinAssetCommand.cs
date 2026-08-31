using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Application.Common.Interfaces;
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

            asset.CurrentAssignmentId = null;
            asset.CheckinCounter++;
            asset.LastCheckin = request.CheckinAt ?? DateTime.UtcNow;
            asset.Status = AssetStatus.Pending; // check-in returns to Pending — Archived stays false (check-in is NOT archiving)
            asset.SystemPositionId = null;
            asset.LocationId = request.LocationId;

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
                logMeta: JsonSerializer.Serialize(new
                {
                    changes = new Dictionary<string, object?>
                    {
                        ["current_assignment_id"] = new { old = oldSnapshot["current_assignment_id"], @new = (string?)null },
                        ["status"] = new { old = oldSnapshot["status"], @new = AssetStatus.Pending.ToString() },
                        ["location_id"] = new { old = oldSnapshot["location_id"]?.ToString(), @new = request.LocationId.ToString() },
                        ["system_position_id"] = new { old = oldSnapshot["system_position_id"]?.ToString(), @new = (string?)null }
                    }
                }));

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new CheckinResult(true, "Asset checked in successfully.");
        });
    }
}