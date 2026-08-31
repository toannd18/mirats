using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Assets.Commands;

public record AcceptAssetCommand(
    Guid AssetId,
    string? Signature,
    Guid CurrentUserId) : IRequest<AcceptDeclineResult>;

public record DeclineAssetCommand(
    Guid AssetId,
    string? Signature,
    Guid CurrentUserId) : IRequest<AcceptDeclineResult>;

public record AcceptDeclineResult(bool Success, string Message, string? ErrorCode = null);

public class AcceptAssetCommandHandler : IRequestHandler<AcceptAssetCommand, AcceptDeclineResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IActionLogService _actionLogService;

    public AcceptAssetCommandHandler(IApplicationDbContext context, IActionLogService actionLogService)
    {
        _context = context;
        _actionLogService = actionLogService;
    }

    public async Task<AcceptDeclineResult> Handle(AcceptAssetCommand request, CancellationToken cancellationToken)
    {
        var asset = await _context.Assets.FirstOrDefaultAsync(a => a.Id == request.AssetId, cancellationToken);

        if (asset == null)
            return new AcceptDeclineResult(false, "Asset not found.", "ASSET_NOT_FOUND");

        if (asset.CurrentAssignmentId == null)
            return new AcceptDeclineResult(false, "Asset is not checked out to you.", "ASSET_NOT_CHECKED_OUT");

        var assignment = await _context.Assignments
            .FirstOrDefaultAsync(a => a.Id == asset.CurrentAssignmentId, cancellationToken);

        if (assignment == null || assignment.TargetType != AssignmentTargetType.User || assignment.TargetId != request.CurrentUserId)
            return new AcceptDeclineResult(false, "Asset is not assigned to you.", "ASSET_NOT_ASSIGNED");

        asset.Accepted = "accepted";

        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.Asset,
            ItemId = request.AssetId,
            TargetType = AssignmentTargetType.User,
            TargetId = request.CurrentUserId,
            ActionType = ActionType.Accept,
            CreatedBy = request.CurrentUserId,
            CompanyId = asset.CompanyId,
            LogMeta = JsonSerializer.Serialize(new { signature = request.Signature }),
            ActionDate = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);

        return new AcceptDeclineResult(true, "Asset acceptance recorded.");
    }
}

public class DeclineAssetCommandHandler : IRequestHandler<DeclineAssetCommand, AcceptDeclineResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IActionLogService _actionLogService;

    public DeclineAssetCommandHandler(IApplicationDbContext context, IActionLogService actionLogService)
    {
        _context = context;
        _actionLogService = actionLogService;
    }

    public async Task<AcceptDeclineResult> Handle(DeclineAssetCommand request, CancellationToken cancellationToken)
    {
        var asset = await _context.Assets.FirstOrDefaultAsync(a => a.Id == request.AssetId, cancellationToken);

        if (asset == null)
            return new AcceptDeclineResult(false, "Asset not found.", "ASSET_NOT_FOUND");

        asset.Accepted = "declined";

        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.Asset,
            ItemId = request.AssetId,
            TargetType = AssignmentTargetType.User,
            TargetId = request.CurrentUserId,
            ActionType = ActionType.Decline,
            CreatedBy = request.CurrentUserId,
            CompanyId = asset.CompanyId,
            LogMeta = JsonSerializer.Serialize(new { signature = request.Signature }),
            ActionDate = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);

        return new AcceptDeclineResult(true, "Asset decline recorded.");
    }
}