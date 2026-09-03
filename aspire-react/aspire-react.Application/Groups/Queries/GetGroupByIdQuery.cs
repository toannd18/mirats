using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Groups.Queries;

public record GroupUserDto(Guid Id, string Username, string Email, string FirstName, string LastName);

public record GroupDetailDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    IReadOnlyList<GroupPermissionDto> Permissions,
    IReadOnlyList<GroupUserDto> Users);

/// <summary>
/// [Giai đoạn 3] GET /api/v1/groups/{id} (extracted from GroupsController.GetGroup).
/// NULL → controller 404 "Group not found." (verbatim). Members listed with their
/// {id, username, email, firstName, lastName} — verbatim shape.
/// </summary>
public record GetGroupByIdQuery(Guid Id) : IRequest<GroupDetailDto?>;

public class GetGroupByIdQueryHandler : IRequestHandler<GetGroupByIdQuery, GroupDetailDto?>
{
    private readonly IApplicationDbContext _context;

    public GetGroupByIdQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<GroupDetailDto?> Handle(GetGroupByIdQuery request, CancellationToken cancellationToken)
    {
        var group = await _context.PermissionGroups
            .Include(g => g.GroupPermissions)
            .Include(g => g.UserGroups).ThenInclude(ug => ug.User)
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == request.Id, cancellationToken);

        if (group == null)
            return null;

        return new GroupDetailDto(
            group.Id,
            group.Name,
            group.Description,
            group.IsSystem,
            group.GroupPermissions.Select(p => new GroupPermissionDto(p.PermissionKey, (int)p.Value)).ToList(),
            group.UserGroups.Select(ug => new GroupUserDto(
                ug.User.Id, ug.User.Username, ug.User.Email, ug.User.FirstName, ug.User.LastName)).ToList());
    }
}
