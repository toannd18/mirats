using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Users.Queries;

public record UserPermissionDto(string PermissionKey, PermissionValue Value);

public record UserGroupMembershipDto(Guid GroupId, string Name);

public record UserDetailDto(
    Guid Id,
    string Username,
    string Email,
    string FirstName,
    string LastName,
    string? EmployeeNumber,
    string? JobTitle,
    bool IsSuperUser,
    bool IsActive,
    Guid? CompanyId,
    string? CompanyName,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? LocationId,
    string? LocationName,
    IReadOnlyList<UserPermissionDto> Permissions,
    IReadOnlyList<UserGroupMembershipDto> Groups,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>
/// [Giai đoạn 3] GET /api/v1/users/{id} (extracted verbatim from UsersController.GetUser).
/// Company-scoping verbatim (Task K): out-of-scope → NULL → controller maps to 404
/// hide-existence. Detail shape verbatim (Permissions as {PermissionKey, Value-enum} +
/// Groups as {GroupId, Name}). NOTE: this file previously declared a handler-less
/// GetUserByIdQuery → UserDto? (dead code, never dispatched — see BACKEND_ARCHITECTURE_REVIEW
/// mục 43); it is now implemented with the verbatim detail shape.
/// </summary>
public record GetUserByIdQuery(Guid Id) : IRequest<UserDetailDto?>;

public class GetUserByIdQueryHandler : IRequestHandler<GetUserByIdQuery, UserDetailDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetUserByIdQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<UserDetailDto?> Handle(GetUserByIdQuery request, CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .Include(u => u.Company)
            .Include(u => u.Department)
            .Include(u => u.Location)
            .Include(u => u.UserPermissions)
            .Include(u => u.UserGroups).ThenInclude(ug => ug.Group)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken);

        // [Task K] verbatim order: lookup first, then scope → 404 hides existence.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (user == null || (userCompanyId.HasValue && user.CompanyId.HasValue && user.CompanyId.Value != userCompanyId.Value))
            return null;

        return new UserDetailDto(
            user.Id,
            user.Username,
            user.Email,
            user.FirstName,
            user.LastName,
            user.EmployeeNumber,
            user.JobTitle,
            user.IsSuperUser,
            user.IsActive,
            user.CompanyId,
            user.Company?.Name,
            user.DepartmentId,
            user.Department?.Name,
            user.LocationId,
            user.Location?.Name,
            user.UserPermissions.Select(p => new UserPermissionDto(p.PermissionKey, p.Value)).ToList(),
            user.UserGroups.Select(ug => new UserGroupMembershipDto(ug.GroupId, ug.Group.Name)).ToList(),
            user.CreatedAt,
            user.UpdatedAt);
    }
}
