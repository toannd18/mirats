using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Assets.Commands;

public record AuditAssetCommand(
    Guid AssetId,
    DateTime? AuditDate,
    string? Note,
    Guid CurrentUserId) : IRequest<AuditResult>;

public record AuditResult(bool Success, string Message, string? ErrorCode = null);

public class AuditAssetCommandHandler : IRequestHandler<AuditAssetCommand, AuditResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IActionLogService _actionLogService;

    public AuditAssetCommandHandler(IApplicationDbContext context, IActionLogService actionLogService)
    {
        _context = context;
        _actionLogService = actionLogService;
    }

    public async Task<AuditResult> Handle(AuditAssetCommand request, CancellationToken cancellationToken)
    {
        var asset = await _context.Assets.FirstOrDefaultAsync(a => a.Id == request.AssetId, cancellationToken);

        if (asset == null)
            return new AuditResult(false, "Asset not found.", "ASSET_NOT_FOUND");

        var oldSnapshot = new Dictionary<string, object?>
        {
            ["last_audit_date"] = asset.LastAuditDate,
            ["next_audit_date"] = asset.NextAuditDate
        };

        var auditDate = request.AuditDate ?? DateTime.UtcNow;
        asset.LastAuditDate = auditDate;
        asset.NextAuditDate = auditDate.AddMonths(12); // Default: audit every 12 months

        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.Asset,
            ItemId = request.AssetId,
            ActionType = ActionType.Audit,
            CreatedBy = request.CurrentUserId,
            CompanyId = asset.CompanyId,
            Note = request.Note,
            LogMeta = JsonSerializer.Serialize(new
            {
                old = oldSnapshot,
                @new = new { last_audit_date = asset.LastAuditDate, next_audit_date = asset.NextAuditDate }
            }),
            ActionDate = auditDate
        });

        await _context.SaveChangesAsync(cancellationToken);

        return new AuditResult(true, "Asset audited successfully.");
    }
}

public record BulkAuditAssetsCommand(
    List<Guid> AssetIds,
    DateTime? AuditDate,
    string? Note,
    Guid CurrentUserId) : IRequest<BulkAuditResult>;

public record BulkAuditResult(bool Success, string Message, int AuditedCount = 0, List<string>? Errors = null);

public class BulkAuditAssetsCommandHandler : IRequestHandler<BulkAuditAssetsCommand, BulkAuditResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IActionLogService _actionLogService;

    public BulkAuditAssetsCommandHandler(IApplicationDbContext context, IActionLogService actionLogService)
    {
        _context = context;
        _actionLogService = actionLogService;
    }

    public async Task<BulkAuditResult> Handle(BulkAuditAssetsCommand request, CancellationToken cancellationToken)
    {
        var assets = await _context.Assets
            .Where(a => request.AssetIds.Contains(a.Id) && a.Status != AssetStatus.Archived)
            .ToListAsync(cancellationToken);

        if (assets.Count == 0)
            return new BulkAuditResult(false, "No valid assets found.", Errors: new List<string> { "All assets not found or archived." });

        var auditDate = request.AuditDate ?? DateTime.UtcNow;

        foreach (var asset in assets)
        {
            var oldSnapshot = new Dictionary<string, object?>
            {
                ["last_audit_date"] = asset.LastAuditDate,
                ["next_audit_date"] = asset.NextAuditDate
            };

            asset.LastAuditDate = auditDate;
            asset.NextAuditDate = auditDate.AddMonths(12);

            _actionLogService.Log(new ActionLogEntry
            {
                ItemType = ItemType.Asset,
                ItemId = asset.Id,
                ActionType = ActionType.Audit,
                CreatedBy = request.CurrentUserId,
                CompanyId = asset.CompanyId,
                Note = request.Note,
                LogMeta = JsonSerializer.Serialize(new
                {
                    old = oldSnapshot,
                    @new = new { last_audit_date = asset.LastAuditDate, next_audit_date = asset.NextAuditDate }
                }),
                ActionDate = auditDate
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new BulkAuditResult(true, $"Successfully audited {assets.Count} assets.", AuditedCount: assets.Count);
    }
}