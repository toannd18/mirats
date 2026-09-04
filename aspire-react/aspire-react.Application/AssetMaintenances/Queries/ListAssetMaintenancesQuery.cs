using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.AssetMaintenances.Queries;

public record AssetMaintenanceListResult(
    string? ErrorCode,
    IReadOnlyList<AssetMaintenanceListItemDto> Items,
    int Total);

/// <summary>
/// [Subtask A] GET api/v1/assets/{assetId}/maintenances (extracted verbatim from
/// AssetMaintenancesController.GetMaintenances). Scope verbatim: asset lookup first —
/// missing → NOT_FOUND ("Asset not found."); regular user + asset in another company →
/// FORBIDDEN (controller maps to Forbid() 403 — NOT 404, verbatim trap (a)). Projection +
/// ordering (StartDate desc, CreatedAt desc) + pagination verbatim.
/// </summary>
public record ListAssetMaintenancesQuery(Guid AssetId, int Page = 1, int PageSize = 20)
    : IRequest<AssetMaintenanceListResult>;

public class ListAssetMaintenancesQueryHandler : IRequestHandler<ListAssetMaintenancesQuery, AssetMaintenanceListResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public ListAssetMaintenancesQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<AssetMaintenanceListResult> Handle(ListAssetMaintenancesQuery request, CancellationToken cancellationToken)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var asset = await _context.Assets.AsNoTracking()
            .Select(a => new { a.Id, a.CompanyId })
            .FirstOrDefaultAsync(a => a.Id == request.AssetId, cancellationToken);
        if (asset == null)
            return new AssetMaintenanceListResult("NOT_FOUND", Array.Empty<AssetMaintenanceListItemDto>(), 0);
        // Regular users may only view maintenances of assets in their own company (floater assets
        // with no company are visible to everyone).
        if (userCompanyId.HasValue && asset.CompanyId.HasValue && asset.CompanyId.Value != userCompanyId.Value)
            return new AssetMaintenanceListResult("FORBIDDEN", Array.Empty<AssetMaintenanceListItemDto>(), 0);

        var query = _context.AssetMaintenances.AsNoTracking()
            .Include(m => m.Supplier)
            .Include(m => m.Asset)
            .Include(m => m.InspectedBy)
            .Include(m => m.Assignees).ThenInclude(a => a.User)
            .Where(m => m.AssetId == request.AssetId && m.DeletedAt == null);

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(m => m.StartDate).ThenByDescending(m => m.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(m => new AssetMaintenanceListItemDto(
                m.Id,
                m.Type.ToString(),
                m.Title,
                m.Notes,
                m.StartDate,
                m.CompletionDate,
                m.Cost,
                m.IsWarranty,
                m.CompanyId,
                m.Supplier == null ? null : new MaintenanceSupplierRefDto(m.Supplier.Id, m.Supplier.Name),
                new MaintenanceAssetRefDto(m.Asset.Id, m.Asset.AssetTag, m.Asset.Name),
                m.SnapshotSystemInfoId,
                m.SnapshotSystemInfoName,
                m.SnapshotSystemPositionId,
                m.SnapshotSystemPositionName,
                m.SnapshotLocationId,
                m.SnapshotLocationName,
                m.SnapshotAssignedUserId,
                m.SnapshotAssignedUserName,
                m.SnapshotDepartmentId,
                m.SnapshotDepartmentName,
                m.InspectedById,
                m.InspectedAt,
                m.InspectedBy != null
                    ? (m.InspectedBy.FirstName + " " + m.InspectedBy.LastName).Trim() != "" ? (m.InspectedBy.FirstName + " " + m.InspectedBy.LastName).Trim() : m.InspectedBy.Username
                    : null,
                m.Assignees.OrderBy(a => a.AssignedAt).Select(a => new MaintenanceAssigneeDto(
                    a.UserId,
                    (a.User.FirstName + " " + a.User.LastName).Trim() != "" ? (a.User.FirstName + " " + a.User.LastName).Trim() : a.User.Username,
                    a.AssignedAt)).ToList(),
                m.CreatedAt,
                m.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new AssetMaintenanceListResult(null, items, total);
    }
}
