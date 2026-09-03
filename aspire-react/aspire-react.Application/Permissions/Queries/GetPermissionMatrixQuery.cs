using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Permissions.Queries;

public record MatrixUserPermissionDto(string PermissionKey, int Value);

public record MatrixGroupPermissionDto(string GroupName, string PermissionKey, int Value);

public record MatrixUserDto(
    Guid Id,
    string Username,
    string Email,
    string FirstName,
    string LastName,
    bool IsSuperUser,
    IReadOnlyList<MatrixUserPermissionDto> UserPermissions,
    IReadOnlyList<MatrixGroupPermissionDto> GroupPermissions);

/// <summary>
/// [Giai đoạn 3] GET /api/v1/permissions/matrix (extracted from
/// PermissionsController.GetPermissionMatrix — admin policy stays on the controller action).
/// Every user with their direct + group-derived permissions (int values — verbatim cast).
/// </summary>
public record GetPermissionMatrixQuery : IRequest<IReadOnlyList<MatrixUserDto>>;

public class GetPermissionMatrixQueryHandler : IRequestHandler<GetPermissionMatrixQuery, IReadOnlyList<MatrixUserDto>>
{
    private readonly IApplicationDbContext _context;

    public GetPermissionMatrixQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<MatrixUserDto>> Handle(GetPermissionMatrixQuery request, CancellationToken cancellationToken)
    {
        var users = await _context.Users
            .Include(u => u.UserPermissions)
            .Include(u => u.UserGroups).ThenInclude(ug => ug.Group).ThenInclude(g => g.GroupPermissions)
            .AsNoTracking()
            .OrderBy(u => u.Username)
            .Select(u => new MatrixUserDto(
                u.Id,
                u.Username,
                u.Email,
                u.FirstName,
                u.LastName,
                u.IsSuperUser,
                u.UserPermissions.Select(p => new MatrixUserPermissionDto(p.PermissionKey, (int)p.Value)).ToList(),
                u.UserGroups.SelectMany(ug =>
                    ug.Group.GroupPermissions.Select(gp => new MatrixGroupPermissionDto(
                        ug.Group.Name,
                        gp.PermissionKey,
                        (int)gp.Value))).ToList()))
            .ToListAsync(cancellationToken);

        return users;
    }
}
