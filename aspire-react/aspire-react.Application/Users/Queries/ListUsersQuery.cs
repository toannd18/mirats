using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Common.Sorting;
using aspire_react.Server.Application.Users.DTOs;
using aspire_react.Server.Domain.Entities;
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
/// [SORT-API] SortBy/SortDir are optional server-side sort (see <see cref="UserListSort"/>);
/// absent/unknown → newest-created first (the previous hardcoded order).
/// </summary>
public record ListUsersQuery(
    string? Search,
    Guid? CompanyId,
    int Page = 1,
    int PageSize = 20,
    string? SortBy = null,
    string? SortDir = null) : IRequest<UserListResult>;

/// <summary>
/// Server-side whitelist sort for the User list (mirrors <c>AssetListSort</c>).
/// Only ENTITY-backed columns are sortable — computed/derived values (full name, role label,
/// status tag) are NOT. <c>username</c> uses the shared PostgreSQL ICU natural collation
/// (<c>und-u-kn-true</c>) so "user2" sorts before "user10"; the default is newest-created first.
/// </summary>
public static class UserListSort
{
    public static IOrderedQueryable<User> Apply(IQueryable<User> query, string? sortBy, bool desc)
    {
        var key = sortBy?.Trim().ToLowerInvariant();
        return key switch
        {
            "username" => desc
                ? query.OrderByDescending(u => EF.Functions.Collate(u.Username, NaturalSort.Collation))
                : query.OrderBy(u => EF.Functions.Collate(u.Username, NaturalSort.Collation)),
            "email" => desc ? query.OrderByDescending(u => u.Email) : query.OrderBy(u => u.Email),
            "firstname" => desc ? query.OrderByDescending(u => u.FirstName) : query.OrderBy(u => u.FirstName),
            "lastname" => desc ? query.OrderByDescending(u => u.LastName) : query.OrderBy(u => u.LastName),
            "createdat" => desc
                ? query.OrderByDescending(u => u.CreatedAt).ThenBy(u => u.Username)
                : query.OrderBy(u => u.CreatedAt).ThenBy(u => u.Username),
            _ => query.OrderByDescending(u => u.CreatedAt).ThenBy(u => u.Username),
        };
    }
}

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

        // [SORT-API] whitelist sort before paging; unknown/absent → newest-created first.
        var desc = string.Equals(request.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        query = UserListSort.Apply(query, request.SortBy, desc);

        var users = await query
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
                HasPassword = u.PasswordHash != null,
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
