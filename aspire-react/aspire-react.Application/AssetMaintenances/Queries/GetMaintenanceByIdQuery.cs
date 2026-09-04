using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.AssetMaintenances.Queries;

public record MaintenanceDetailResult(string? ErrorCode, MaintenanceDetailDto? Detail);

/// <summary>
/// [Subtask A] GET api/v1/maintenances/{id} (extracted verbatim from
/// AssetMaintenancesController.GetMaintenance). Missing/soft-deleted → NOT_FOUND
/// ("Maintenance not found."); regular user + other-company record → FORBIDDEN (controller
/// maps to Forbid() 403 — verbatim trap (a), NOT 404). Detail projection + live
/// currentContext (computed on the fly via the ported snapshot builder) verbatim.
/// </summary>
public record GetMaintenanceByIdQuery(Guid Id) : IRequest<MaintenanceDetailResult>;

public class GetMaintenanceByIdQueryHandler : IRequestHandler<GetMaintenanceByIdQuery, MaintenanceDetailResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetMaintenanceByIdQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<MaintenanceDetailResult> Handle(GetMaintenanceByIdQuery request, CancellationToken cancellationToken)
    {
        var m = await _context.AssetMaintenances.AsNoTracking()
            .Include(x => x.Supplier)
            .Include(x => x.Asset.SystemPosition).ThenInclude(sp => sp.SystemInfo)
            .Include(x => x.Asset.Location)
            .Include(x => x.Asset.CurrentAssignment)
            .Include(x => x.InspectedBy)
            .Include(x => x.Assignees).ThenInclude(a => a.User)
            .FirstOrDefaultAsync(x => x.Id == request.Id && x.DeletedAt == null, cancellationToken);
        if (m == null)
            return new MaintenanceDetailResult("NOT_FOUND", null);

        // 403 (not 404): the record exists but the regular user's company cannot view it.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && m.CompanyId != userCompanyId.Value && m.CompanyId != Guid.Empty)
            return new MaintenanceDetailResult("FORBIDDEN", null);

        // LIVE context of the asset RIGHT NOW (computed on the fly, never stored) so viewers can
        // compare "how it was during maintenance" (Snapshot*) vs "how it is today".
        var cur = await MaintenanceSnapshot.BuildAsync(_context, m.Asset, cancellationToken);

        return new MaintenanceDetailResult(null, new MaintenanceDetailDto(
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
            m.UpdatedAt,
            new MaintenanceCurrentContextDto(
                cur.SysInfoId, cur.SysInfoName, cur.PosId, cur.PosName, cur.LocId, cur.LocName,
                cur.UserId, cur.UserName, cur.DeptId, cur.DeptName)));
    }
}

/// <summary>
/// Ported verbatim from AssetMaintenancesController.BuildSnapshotAsync (subtask A — read-only
/// use). Captures the asset's context at THIS moment: SystemPosition + parent SystemInfo,
/// Location, assigned User, Department. Only the DbContext source changed
/// (AppDbContext → IApplicationDbContext); logic identical. Shared by the detail query now,
/// and by the Create command in subtask B.
/// </summary>
internal static class MaintenanceSnapshot
{
    internal static async Task<(Guid? SysInfoId, string? SysInfoName, Guid? PosId, string? PosName,
        Guid? LocId, string? LocName, Guid? UserId, string? UserName, Guid? DeptId, string? DeptName)>
        BuildAsync(IApplicationDbContext context, Asset asset, CancellationToken ct)
    {
        Guid? sysInfoId = null;
        string? sysInfoName = null;
        Guid? posId = null;
        string? posName = null;
        if (asset.SystemPosition != null)
        {
            posId = asset.SystemPosition.Id;
            posName = asset.SystemPosition.Name;
            sysInfoId = asset.SystemPosition.SystemInfoId;
            sysInfoName = asset.SystemPosition.SystemInfo?.Name;
        }

        Guid? locId = asset.LocationId;
        string? locName = asset.Location?.Name;

        Guid? userId = null;
        string? userName = null;
        Guid? deptId = null;
        string? deptName = null;
        if (asset.CurrentAssignment != null)
        {
            var asgn = asset.CurrentAssignment;
            if (asgn.TargetType == AssignmentTargetType.User)
            {
                userId = asgn.TargetId;
                var user = await context.Users.AsNoTracking()
                    .Where(u => u.Id == userId.Value)
                    .Select(u => new
                    {
                        DisplayName = (u.FirstName + " " + u.LastName).Trim() != ""
                            ? (u.FirstName + " " + u.LastName).Trim()
                            : u.Username,
                        u.DepartmentId,
                        DeptName = u.Department != null ? u.Department.Name : (string?)null
                    })
                    .FirstOrDefaultAsync(ct);
                if (user != null)
                {
                    userName = user.DisplayName;
                    deptId = user.DepartmentId;
                    deptName = user.DeptName;
                }
            }
            else if (asgn.TargetType == AssignmentTargetType.Department)
            {
                deptId = asgn.TargetId;
                deptName = await context.Departments.AsNoTracking()
                    .Where(d => d.Id == deptId.Value)
                    .Select(d => d.Name)
                    .FirstOrDefaultAsync(ct);
            }
        }

        return (sysInfoId, sysInfoName, posId, posName, locId, locName, userId, userName, deptId, deptName);
    }
}
