using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Users.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Users.Queries;

/// <summary>
/// [Giai đoạn 3] GET /api/v1/users/me (extracted verbatim from UsersController.GetCurrentUser).
/// Takes the already-resolved local user id (controller parses the local_user_id claim and maps
/// absent/invalid → Unauthorized BEFORE sending). NULL → controller maps to Unauthorized
/// (fail closed, verbatim). Projection verbatim (UserDto + Groups).
/// </summary>
public record GetCurrentUserQuery(Guid UserId) : IRequest<UserDto?>;

public class GetCurrentUserQueryHandler : IRequestHandler<GetCurrentUserQuery, UserDto?>
{
    private readonly IApplicationDbContext _context;

    public GetCurrentUserQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<UserDto?> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .Include(u => u.Company)
            .Include(u => u.Department)
            .Include(u => u.Location)
            .Include(u => u.UserGroups).ThenInclude(ug => ug.Group)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

        if (user == null)
            return null;

        return new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            EmployeeNumber = user.EmployeeNumber,
            JobTitle = user.JobTitle,
            IsSuperUser = user.IsSuperUser,
            IsActive = user.IsActive,
            CompanyId = user.CompanyId,
            CompanyName = user.Company?.Name,
            DepartmentId = user.DepartmentId,
            DepartmentName = user.Department?.Name,
            LocationId = user.LocationId,
            LocationName = user.Location?.Name,
            Groups = user.UserGroups.Select(ug => new UserGroupDto(ug.GroupId, ug.Group.Name, ug.Group.IsSystem)).ToList(),
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
        };
    }
}
