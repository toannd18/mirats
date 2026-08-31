using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Assets.Commands;

public record DeleteAssetCommand : IRequest<AssetResult>
{
    public Guid AssetId { get; init; }
    public Guid CurrentUserId { get; init; }
}

public class DeleteAssetCommandHandler : IRequestHandler<DeleteAssetCommand, AssetResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IActionLogService _actionLogService;
    private readonly ICompanyScopeService _companyScope;

    public DeleteAssetCommandHandler(IApplicationDbContext context, IActionLogService actionLogService, ICompanyScopeService companyScope)
    {
        _context = context;
        _actionLogService = actionLogService;
        _companyScope = companyScope;
    }

    public async Task<AssetResult> Handle(DeleteAssetCommand request, CancellationToken cancellationToken)
    {
        var asset = await _context.Assets.FirstOrDefaultAsync(a => a.Id == request.AssetId, cancellationToken);

        if (asset == null)
            return new AssetResult(false, "Asset not found.", ErrorCode: "NOT_FOUND");

        // Company scoping: a regular user may only delete assets of their own company (or floater).
        // Superuser (userCompanyId == null) bypasses. Same "hide existence" convention as GetAsset.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && asset.CompanyId.HasValue && asset.CompanyId.Value != userCompanyId.Value)
            return new AssetResult(false, "Asset not found.", ErrorCode: "NOT_FOUND");

        // Cannot delete confirmed assets
        if (asset.IsConfirmed)
            return new AssetResult(false, "Không thể xóa tài sản đã được xác nhận.", ErrorCode: "ASSET_CONFIRMED_CANNOT_DELETE");

        // Cannot delete assets that are currently checked out
        if (asset.CurrentAssignmentId != null)
            return new AssetResult(false, "Không thể xóa tài sản đang được cấp phát. Vui lòng thu hồi trước.", ErrorCode: "ASSET_CHECKED_OUT");

        // Delete guard: an asset with historical transactional data cannot be deleted. Hard-deleting
        // would cascade-remove it across assignments / maintenance records / component allocations
        // (FK CASCADE) → assignment + maintenance history loss. (ActionLog rows have no FK to Asset,
        // so they survive — but the assignments/maintenance/component links must be preserved.)
        if (await _context.Assignments.AnyAsync(a => a.AssetId == request.AssetId, cancellationToken))
            return new AssetResult(false, "Không thể xóa tài sản đã từng được cấp phát (lịch sử điều phối phải được giữ).", ErrorCode: "ASSET_HAS_ASSIGNMENTS");
        if (await _context.AssetMaintenances.AnyAsync(m => m.AssetId == request.AssetId && m.DeletedAt == null, cancellationToken))
            return new AssetResult(false, "Không thể xóa tài sản đã có phiếu bảo trì (lịch sử bảo trì phải được giữ).", ErrorCode: "ASSET_HAS_MAINTENANCES");
        if (await _context.ComponentAssignments.AnyAsync(ca => ca.AssetId == request.AssetId, cancellationToken))
            return new AssetResult(false, "Không thể xóa tài sản đang được linh kiện (Component) sử dụng.", ErrorCode: "ASSET_USED_BY_COMPONENT");

        // Log before removal
        _actionLogService.LogAction(
            itemType: ItemType.Asset,
            itemId: asset.Id,
            actionType: ActionType.Delete,
            loggedByUserId: request.CurrentUserId,
            companyId: asset.CompanyId,
            note: $"Deleted asset: {asset.AssetTag} - {asset.Name}",
            logMeta: JsonSerializer.Serialize(new { assetTag = asset.AssetTag, name = asset.Name, serial = asset.Serial }));

        _context.Assets.Remove(asset);
        await _context.SaveChangesAsync(cancellationToken);

        return new AssetResult(true, "Asset deleted successfully.");
    }
}