using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Assets.Commands;

public record BulkUpdateAssetsCommand(
    List<Guid> AssetIds,
    Guid? LocationId,
    Guid CurrentUserId) : IRequest<BulkUpdateResult>;

public record BulkUpdateResult(bool Success, string Message, int UpdatedCount = 0, List<string>? Errors = null);

public class BulkUpdateAssetsCommandHandler : IRequestHandler<BulkUpdateAssetsCommand, BulkUpdateResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IActionLogService _actionLogService;

    public BulkUpdateAssetsCommandHandler(IApplicationDbContext context, IActionLogService actionLogService)
    {
        _context = context;
        _actionLogService = actionLogService;
    }

    public async Task<BulkUpdateResult> Handle(BulkUpdateAssetsCommand request, CancellationToken cancellationToken)
    {
        var assets = await _context.Assets
            .Where(a => request.AssetIds.Contains(a.Id) && a.Status != AssetStatus.Archived)
            .ToListAsync(cancellationToken);

        if (assets.Count == 0)
            return new BulkUpdateResult(false, "No valid assets found.", Errors: new List<string> { "All assets not found or archived." });

        foreach (var asset in assets)
        {
            var oldSnapshot = new Dictionary<string, object?>
            {
                ["location_id"] = asset.LocationId
            };

            if (request.LocationId.HasValue) asset.LocationId = request.LocationId.Value;

            _actionLogService.Log(new ActionLogEntry
            {
                ItemType = ItemType.Asset,
                ItemId = asset.Id,
                ActionType = ActionType.Update,
                CreatedBy = request.CurrentUserId,
                CompanyId = asset.CompanyId,
                LogMeta = JsonSerializer.Serialize(new
                {
                    changes = new Dictionary<string, object?>
                    {
                        ["location_id"] = new { old = oldSnapshot["location_id"]?.ToString(), @new = asset.LocationId?.ToString() }
                    }
                })
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
        return new BulkUpdateResult(true, $"Successfully updated {assets.Count} assets.", UpdatedCount: assets.Count);
    }
}