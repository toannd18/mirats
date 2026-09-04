using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Users.DTOs;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Users.Queries;

public record UserListResult(IReadOnlyList<UserDto> Items, int Total);

/// <summary>
/// [Giai đoạn 3] GET /api/v1/users (extracted verbatim from UsersController.GetUsers).
/// Company-scoping verbatim (Task K): regular user forced to own-company/floater — the optional
/// companyId query param is IGNORED for a regular user; superuser (scope → null) may optionally
/// filter by companyId. Search/order/pagination/projection verbatim (UserDto + Groups).
/// </summary>
public record ListUsersQuery(string? Search, Guid? CompanyId, int Page = 1, int PageSize = 20)
    : IRequest<UserListResult>;

public class ListUsersQueryHandler : IRequestHandler<ListUsersQuery, UserListResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public ListUsersQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<UserListResult> Handle(ListUsersQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Users
            .Include(u => u.Company)
            .Include(u => u.Department)
            .Include(u => u.Location)
            .Include(u => u.UserGroups).ThenInclude(ug => ug.Group)
            .AsNoTracking();

        // [Task K] verbatim: regular user forced to own scope; superuser may filter by companyId.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue)
            query = query.Where(u => u.CompanyId == null || u.CompanyId == userCompanyId.Value);
        else if (request.CompanyId.HasValue)
            query = query.Where(u => u.CompanyId == request.CompanyId.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var searchLower = request.Search.ToLower();
            query = query.Where(u =>
                u.Username.ToLower().Contains(searchLower) ||
                u.Email.ToLower().Contains(searchLower) ||
                u.FirstName.ToLower().Contains(searchLower) ||
                u.LastName.ToLower().Contains(searchLower) ||
                (u.JobTitle != null && u.JobTitle.ToLower().Contains(searchLower)));
        }

        var total = await query.CountAsync(cancellationToken);

        var users = await query
            .OrderBy(u => u.Username)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(u => new UserDto
            {
                Id = u.Id,
                Username = u.Username,
                Email = u.Email,
                FirstName = u.FirstName,
                LastName = u.LastName,
                EmployeeNumber = u.EmployeeNumber,
                JobTitle = u.JobTitle,
                IsSuperUser = u.IsSuperUser,
                IsActive = u.IsActive,
                CompanyId = u.CompanyId,
                CompanyName = u.Company != null ? u.Company.Name : null,
                DepartmentId = u.DepartmentId,
                DepartmentName = u.Department != null ? u.Department.Name : null,
                LocationId = u.LocationId,
                LocationName = u.Location != null ? u.Location.Name : null,
                Groups = u.UserGroups.Select(ug => new UserGroupDto(ug.GroupId, ug.Group.Name, ug.Group.IsSystem)).ToList(),
                CreatedAt = u.CreatedAt,
                UpdatedAt = u.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        return new UserListResult(users, total);
    }
}
