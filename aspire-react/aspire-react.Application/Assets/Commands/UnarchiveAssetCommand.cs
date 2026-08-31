using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Application.Common.Interfaces;
using MediatR;

namespace aspire_react.Server.Application.Assets.Commands;

public record UnarchiveAssetCommand(Guid AssetId, Guid CurrentUserId, string? Note = null) : IRequest<AssetResult>;

public class UnarchiveAssetCommandHandler : IRequestHandler<UnarchiveAssetCommand, AssetResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IActionLogService _actionLogService;

    public UnarchiveAssetCommandHandler(IApplicationDbContext context, IActionLogService actionLogService)
    {
        _context = context;
        _actionLogService = actionLogService;
    }

    public async Task<AssetResult> Handle(UnarchiveAssetCommand request, CancellationToken cancellationToken)
    {
        var asset = await _context.Assets.FindAsync([request.AssetId], cancellationToken);
        if (asset == null)
            return new AssetResult(false, "Asset not found.", ErrorCode: "NOT_FOUND");

        if (asset.Status != AssetStatus.Archived)
            return new AssetResult(false, "Only archived assets can be unarchived.", ErrorCode: "NOT_ARCHIVED");

        var oldLocationId = asset.LocationId;

        asset.Status = AssetStatus.Pending;
        // Keep existing LocationId — no forced relocation on unarchive

        _actionLogService.LogAction(
            itemType: ItemType.Asset,
            itemId: asset.Id,
            actionType: ActionType.Unarchive,
            loggedByUserId: request.CurrentUserId,
            locationId: asset.LocationId,
            companyId: asset.CompanyId,
            note: request.Note ?? "Asset unarchived",
            logMeta: JsonSerializer.Serialize(new
            {
                changes = new Dictionary<string, object?>
                {
                    ["status"] = new { old = AssetStatus.Archived.ToString(), @new = AssetStatus.Pending.ToString() },
                    ["locationId"] = new { old = oldLocationId?.ToString(), @new = asset.LocationId?.ToString() }
                }
            }));

        await _context.SaveChangesAsync(cancellationToken);
        return new AssetResult(true, "Asset unarchived successfully.", AssetId: asset.Id);
    }
}