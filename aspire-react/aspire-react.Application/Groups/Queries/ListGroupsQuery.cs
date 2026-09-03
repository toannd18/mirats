using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Groups.Queries;

public record GroupPermissionDto(string PermissionKey, int Value);

public record GroupListItemDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<GroupPermissionDto> Permissions,
    int UserCount);

/// <summary>
/// [Giai đoạn 3] GET /api/v1/groups (extracted from GroupsController.GetGroups — admin-only
/// controller, no company-scoping by design: PermissionGroup is system-wide). Shape parity
/// note: Permissions[].Value is the INT enum value (verbatim pre-migration cast — a known
/// convention inconsistency, see BACKLOG BUG-K notes).
/// </summary>
public record ListGroupsQuery : IRequest<IReadOnlyList<GroupListItemDto>>;

public class ListGroupsQueryHandler : IRequestHandler<ListGroupsQuery, IReadOnlyList<GroupListItemDto>>
{
    private readonly IApplicationDbContext _context;

    public ListGroupsQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<GroupListItemDto>> Handle(ListGroupsQuery request, CancellationToken cancellationToken)
    {
        var groups = await _context.PermissionGroups
            .Include(g => g.GroupPermissions)
            .Include(g => g.UserGroups).ThenInclude(ug => ug.User)
            .AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new GroupListItemDto(
                g.Id,
                g.Name,
                g.Description,
                g.IsSystem,
                g.CreatedAt,
                g.UpdatedAt,
                g.GroupPermissions.Select(p => new GroupPermissionDto(p.PermissionKey, (int)p.Value)).ToList(),
                g.UserGroups.Count))
            .ToListAsync(cancellationToken);

        return groups;
    }
}
