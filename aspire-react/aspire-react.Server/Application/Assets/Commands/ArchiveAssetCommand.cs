using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using MediatR;

namespace aspire_react.Server.Application.Assets.Commands;

public record ArchiveAssetCommand(Guid AssetId, Guid LocationId, Guid CurrentUserId, string? Note = null) : IRequest<AssetResult>;

public class ArchiveAssetCommandHandler : IRequestHandler<ArchiveAssetCommand, AssetResult>
{
    private readonly AppDbContext _context;
    private readonly IActionLogService _actionLogService;

    public ArchiveAssetCommandHandler(AppDbContext context, IActionLogService actionLogService)
    {
        _context = context;
        _actionLogService = actionLogService;
    }

    public async Task<AssetResult> Handle(ArchiveAssetCommand request, CancellationToken cancellationToken)
    {
        var asset = await _context.Assets.FindAsync([request.AssetId], cancellationToken);
        if (asset == null)
            return new AssetResult(false, "Asset not found.", ErrorCode: "NOT_FOUND");

        if (asset.Status == AssetStatus.Archived)
            return new AssetResult(false, "Asset is already archived.", ErrorCode: "ALREADY_ARCHIVED");

        var oldStatus = asset.Status;
        var oldLocationId = asset.LocationId;

        // Archive (Lưu trữ / thanh lý) is an explicit, terminal action — it is NOT triggered
        // by a check-in. LocationId is required (storage/disposal location).
        asset.Status = AssetStatus.Archived;
        asset.LocationId = request.LocationId;
        asset.CurrentAssignmentId = null;
        asset.SystemPositionId = null;

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

        await _context.SaveChangesAsync(cancellationToken);
        return new AssetResult(true, "Asset archived successfully.", AssetId: asset.Id);
    }
}