using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Systems.Queries;

public record SystemAssetPositionDto(Guid Id, string Code, string Name);

public record SystemAssetCompanyDto(Guid Id, string Name);

public record SystemAssetLocationDto(Guid Id, string Name);

public record SystemAssetAssignedToDto(string Type, Guid TargetId, string? Name);

public record SystemAssetDepartmentDto(Guid Id, string Name);

public record SystemAssetRowDto(
    Guid Id,
    string AssetTag,
    string Name,
    string? Serial,
    string Status,
    SystemAssetPositionDto? SystemPosition,
    SystemAssetLocationDto? Location,
    SystemAssetCompanyDto? Company,
    SystemAssetAssignedToDto? AssignedTo,
    SystemAssetDepartmentDto? Department);

public record SystemAssetsResult(IReadOnlyList<SystemAssetRowDto> Items, int Total);

/// <summary>
/// [Giai đoạn 3] GET /api/v1/systems/{id}/assets (extracted from SystemsController.GetAssets).
/// Assets installed across ALL child SystemPositions of the system (optional position narrow —
/// position quick-filter). Defense-in-depth company filter verbatim; assigned-to names resolved
/// per TargetType (User/Department/SystemPosition) + the assigned user's department chain —
/// verbatim batch resolution. No page clamps (verbatim).
/// </summary>
public record GetSystemAssetsQuery(Guid Id, Guid? SystemPositionId = null, int Page = 1, int PageSize = 20)
    : IRequest<SystemAssetsResult?>;

public class GetSystemAssetsQueryHandler : IRequestHandler<GetSystemAssetsQuery, SystemAssetsResult?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetSystemAssetsQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<SystemAssetsResult?> Handle(GetSystemAssetsQuery request, CancellationToken cancellationToken)
    {
        if (!await SystemsVisibility.IsSystemVisibleAsync(_context, _companyScope, request.Id, cancellationToken))
            return null;

        var query = _context.Assets.AsNoTracking()
            .Include(a => a.Location)
            .Include(a => a.Company)
            .Include(a => a.SystemPosition)
            .Include(a => a.CurrentAssignment)
            .Where(a => a.SystemPosition != null && a.SystemPosition.SystemInfoId == request.Id);

        if (request.SystemPositionId.HasValue)
            query = query.Where(a => a.SystemPositionId == request.SystemPositionId.Value);

        // Defense in depth (same as Asset Maintenance list): a regular user with a configured company
        // may only see assets of their own company; company-less floaters are visible to everyone.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue)
            query = query.Where(a => a.CompanyId == null || a.CompanyId == userCompanyId.Value);

        var total = await query.CountAsync(cancellationToken);
        var assets = await query.OrderBy(a => a.AssetTag).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(a => new
            {
                a.Id,
                a.AssetTag,
                a.Name,
                a.Serial,
                Status = a.Status.ToString(),
                SystemPosition = a.SystemPosition == null
                    ? null
                    : new { a.SystemPosition.Id, a.SystemPosition.Code, a.SystemPosition.Name },
                Location = a.Location == null ? null : new { a.Location.Id, a.Location.Name },
                Company = a.Company == null ? null : new { a.Company.Id, a.Company.Name },
                AssignedTo = a.CurrentAssignment == null
                    ? null
                    : new { type = a.CurrentAssignment.TargetType.ToString(), targetId = a.CurrentAssignment.TargetId }
            })
            .ToListAsync(cancellationToken);

        // Batch-resolve assigned-to names + the assigned user's department (mirrors AssetsController).
        var atAssets = assets.Where(a => a.AssignedTo != null).Select(a => a.AssignedTo!).ToList();
        var uDict = new Dictionary<Guid, string>();
        var dDict = new Dictionary<Guid, string>();
        var pDict = new Dictionary<Guid, string>();
        var deptOfUser = new Dictionary<Guid, Guid?>();
        if (atAssets.Any())
        {
            var uids = atAssets.Where(x => x.type == "User").Select(x => x.targetId).Distinct().ToList();
            var dids = atAssets.Where(x => x.type == "Department").Select(x => x.targetId).Distinct().ToList();
            var pids = atAssets.Where(x => x.type == "SystemPosition").Select(x => x.targetId).Distinct().ToList();
            if (uids.Any())
            {
                var users = await _context.Users.AsNoTracking()
                    .Where(u => uids.Contains(u.Id))
                    .Select(u => new
                    {
                        u.Id,
                        Display = (u.FirstName + " " + u.LastName).Trim() != "" ? (u.FirstName + " " + u.LastName).Trim() : u.Username,
                        u.DepartmentId
                    })
                    .ToListAsync(cancellationToken);
                foreach (var u in users)
                {
                    uDict[u.Id] = u.Display;
                    deptOfUser[u.Id] = u.DepartmentId;
                }
            }
            if (dids.Any())
                dDict = await _context.Departments.Where(d => dids.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);
            if (pids.Any())
                pDict = await _context.SystemPositions.Where(sp => pids.Contains(sp.Id)).ToDictionaryAsync(sp => sp.Id, sp => sp.Name, cancellationToken);
        }
        var deptIds = deptOfUser.Values.Where(v => v.HasValue).Select(v => v!.Value).Distinct().ToList();
        var deptNameDict = new Dictionary<Guid, string>();
        if (deptIds.Any())
            deptNameDict = await _context.Departments.Where(d => deptIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);
        var enriched = assets.Select(a =>
        {
            string? an = null;
            Guid? assignedDeptId = null;
            if (a.AssignedTo != null)
            {
                an = a.AssignedTo.type switch
                {
                    "User" => uDict.GetValueOrDefault(a.AssignedTo.targetId),
                    "Department" => dDict.GetValueOrDefault(a.AssignedTo.targetId),
                    "SystemPosition" => pDict.GetValueOrDefault(a.AssignedTo.targetId),
                    _ => null
                };
                if (a.AssignedTo.type == "User")
                    assignedDeptId = deptOfUser.GetValueOrDefault(a.AssignedTo.targetId);
            }
            return new SystemAssetRowDto(
                a.Id,
                a.AssetTag,
                a.Name,
                a.Serial,
                a.Status,
                a.SystemPosition == null ? null : new SystemAssetPositionDto(a.SystemPosition.Id, a.SystemPosition.Code, a.SystemPosition.Name),
                a.Location == null ? null : new SystemAssetLocationDto(a.Location.Id, a.Location.Name),
                a.Company == null ? null : new SystemAssetCompanyDto(a.Company.Id, a.Company.Name),
                a.AssignedTo == null ? null : new SystemAssetAssignedToDto(a.AssignedTo.type, a.AssignedTo.targetId, an),
                assignedDeptId.HasValue && deptNameDict.TryGetValue(assignedDeptId.Value, out var dn)
                    ? new SystemAssetDepartmentDto(assignedDeptId.Value, dn)
                    : null);
        }).ToList();

        return new SystemAssetsResult(enriched, total);
    }
}
