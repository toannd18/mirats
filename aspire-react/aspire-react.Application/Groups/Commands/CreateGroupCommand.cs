using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;

namespace aspire_react.Server.Application.Groups.Commands;

/// <summary>
/// [Giai đoạn 3] POST /api/v1/groups (extracted from GroupsController.Create).
/// ⚠️ TODO BUG-K (MEDIUM, docs/BACKLOG.md): NO duplicate-Name check and NO empty-Name check —
/// verbatim pre-migration behavior (creating a group with an existing/empty name succeeds).
/// PermissionGroup is system-wide (no CompanyId) → log CompanyId = null.
/// ILoggableCommand only (no output-cache on groups).
/// </summary>
public record CreateGroupCommand(string Name, string? Description, Guid CurrentUserId)
    : IRequest<GroupResult>, ILoggableCommand<GroupResult>
{
    public ActionLogEntry? BuildLogEntry(GroupResult response)
    {
        if (!response.Success)
            return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.PermissionGroup,
            ItemId = response.GroupId!.Value,
            ActionType = ActionType.Create,
            CreatedBy = CurrentUserId,
            CompanyId = null, // PermissionGroup has no CompanyId — company-independent log
            Note = $"Created group: {response.Name}"
        };
    }
}

public class CreateGroupCommandHandler : IRequestHandler<CreateGroupCommand, GroupResult>
{
    private readonly IApplicationDbContext _context;

    public CreateGroupCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<GroupResult> Handle(CreateGroupCommand request, CancellationToken cancellationToken)
    {
        var group = new PermissionGroup
        {
            Name = request.Name,
            Description = request.Description
        };

        _context.PermissionGroups.Add(group);
        await _context.SaveChangesAsync(cancellationToken);
        // Old controller wrote the log in the SAME SaveChanges as the data — ActionLogBehavior
        // preserves that atomicity (log written by the behavior inside the ambient transaction).

        return new GroupResult(true, "Group created successfully.", GroupId: group.Id, Name: group.Name, IsSystem: group.IsSystem);
    }
}
