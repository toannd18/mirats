using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.AssetMaintenances.Queries;

public record AllMaintenanceListResult(IReadOnlyList<AllMaintenanceListItemDto> Items, int Total);

/// <summary>
/// [Subtask A] GET api/v1/maintenances (extracted verbatim from
/// AssetMaintenancesController.GetAllMaintenances). Filters verbatim: optional assetId plus
/// the SnapshotSystemInfoId system filter (history stays correct after re-parenting).
/// Company-scoping verbatim: regular user sees own-company OR floater sentinel
/// (m.CompanyId == Guid.Empty — maintenance CompanyId is non-nullable, server-set);
/// superuser (scope → null) sees all. No failure modes (always 200).
/// SortBy/SortDir mirror the Asset list contract (whitelist in <see cref="MaintenanceListSort"/>);
/// omitted/unknown values fall back to newest-created first (CreatedAt desc).
/// </summary>
public record ListAllMaintenancesQuery(
    Guid? AssetId,
    Guid? SystemInfoId,
    int Page = 1,
    int PageSize = 20,
    string? SortBy = null,
    string? SortDir = null) : IRequest<AllMaintenanceListResult>;

/// <summary>
/// Whitelist server-side sort for the maintenance list (pattern: <c>AssetListSort</c>).
/// Accepted keys (case-insensitive): <c>title</c>, <c>startdate</c>, <c>completiondate</c>,
/// <c>cost</c>, <c>createdat</c>. Default (no/unknown key) = newest-created first.
/// Nullable columns (CompletionDate, Cost) keep NULLs last in BOTH directions so empty values
/// never float to the top of the list.
/// </summary>
public static class MaintenanceListSort
{
    public static IOrderedQueryable<AssetMaintenance> Apply(IQueryable<AssetMaintenance> query, string? sortBy, bool desc)
    {
        var key = sortBy?.Trim().ToLowerInvariant();
        return key switch
        {
            "title" => desc ? query.OrderByDescending(m => m.Title) : query.OrderBy(m => m.Title),
            "startdate" => desc ? query.OrderByDescending(m => m.StartDate) : query.OrderBy(m => m.StartDate),
            "completiondate" => desc
                ? query.OrderByDescending(m => m.CompletionDate.HasValue).ThenByDescending(m => m.CompletionDate)
                : query.OrderByDescending(m => m.CompletionDate.HasValue).ThenBy(m => m.CompletionDate),
            "cost" => desc
                ? query.OrderByDescending(m => m.Cost.HasValue).ThenByDescending(m => m.Cost)
                : query.OrderByDescending(m => m.Cost.HasValue).ThenBy(m => m.Cost),
            "createdat" => desc ? query.OrderByDescending(m => m.CreatedAt) : query.OrderBy(m => m.CreatedAt),
            _ => query.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.StartDate),
        };
    }
}

public class ListAllMaintenancesQueryHandler : IRequestHandler<ListAllMaintenancesQuery, AllMaintenanceListResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public ListAllMaintenancesQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<AllMaintenanceListResult> Handle(ListAllMaintenancesQuery request, CancellationToken cancellationToken)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();

        var query = _context.AssetMaintenances.AsNoTracking()
            .Include(m => m.Supplier)
            .Include(m => m.Asset)
            .Include(m => m.Asset.Company)
            .Include(m => m.InspectedBy)
            .Include(m => m.Assignees).ThenInclude(a => a.User)
            .Where(m => m.DeletedAt == null);
        if (request.AssetId.HasValue)
            query = query.Where(m => m.AssetId == request.AssetId.Value);
        if (request.SystemInfoId.HasValue)
            query = query.Where(m => m.SnapshotSystemInfoId == request.SystemInfoId.Value);
        if (userCompanyId.HasValue)
            query = query.Where(m => m.CompanyId == userCompanyId.Value || m.CompanyId == Guid.Empty);

        var total = await query.CountAsync(cancellationToken);

        // Whitelist sort (pattern: AssetListSort). No/unknown SortBy → newest-created first.
        var desc = string.Equals(request.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        query = MaintenanceListSort.Apply(query, request.SortBy, desc);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(m => new AllMaintenanceListItemDto(
                m.Id,
                m.Type.ToString(),
                m.Title,
                m.Notes,
                m.StartDate,
                m.CompletionDate,
                m.Cost,
                m.IsWarranty,
                m.IsClosed,
                m.ClosedAt,
                m.ClosedById,
                m.CompanyId,
                m.Supplier == null ? null : new MaintenanceSupplierRefDto(m.Supplier.Id, m.Supplier.Name),
                new MaintenanceAssetWithCompanyRefDto(m.Asset.Id, m.Asset.AssetTag, m.Asset.Name, m.Asset.Company != null ? m.Asset.Company.Name : null),
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

        return new AllMaintenanceListResult(items, total);
    }
}
