using aspire_react.Server.Application.Common.ActionLogs;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Application.Common.Interfaces;
using MediatR;

namespace aspire_react.Server.Application.Assets.Commands;

public record ConfirmAssetCommand(Guid AssetId, Guid CurrentUserId) : IRequest<AssetResult>;

public class ConfirmAssetCommandHandler : IRequestHandler<ConfirmAssetCommand, AssetResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IActionLogService _actionLogService;

    public ConfirmAssetCommandHandler(IApplicationDbContext context, IActionLogService actionLogService)
    {
        _context = context;
        _actionLogService = actionLogService;
    }

    public async Task<AssetResult> Handle(ConfirmAssetCommand request, CancellationToken cancellationToken)
    {
        var asset = await _context.Assets.FindAsync([request.AssetId], cancellationToken);
        if (asset == null)
            return new AssetResult(false, "Asset not found.", ErrorCode: "NOT_FOUND");

        if (asset.IsConfirmed)
            return new AssetResult(false, "Asset is already confirmed.", ErrorCode: "ALREADY_CONFIRMED");

        var oldStatus = asset.Status;
        var changes = new ActionLogChanges()
            .Track("isConfirmed", false)
            .Track("status", oldStatus.ToString());

        asset.IsConfirmed = true;
        asset.Status = AssetStatus.Pending; // officially ready for checkout

        _actionLogService.LogAction(
            itemType: ItemType.Asset,
            itemId: asset.Id,
            actionType: ActionType.Confirm,
            loggedByUserId: request.CurrentUserId,
            companyId: asset.CompanyId,
            note: $"Xác nhận thiết bị: {asset.AssetTag} - {asset.Name}",
            logMeta: changes
                .Set("isConfirmed", true)
                .Set("status", asset.Status.ToString())
                .ToLogMeta());

        await _context.SaveChangesAsync(cancellationToken);

        return new AssetResult(true, "Asset confirmed successfully.", AssetId: asset.Id);
    }
}