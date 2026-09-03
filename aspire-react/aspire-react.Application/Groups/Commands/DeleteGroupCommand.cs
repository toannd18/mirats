using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;

namespace aspire_react.Server.Application.Groups.Commands;

/// <summary>
/// [Giai đoạn 3] DELETE /api/v1/groups/{id} (extracted from GroupsController.Delete).
/// Guards verbatim: SYSTEM_GROUP_LOCKED (IsSystem) → SELF_LOCKOUT via IPermissionLockoutGuard
/// (WouldDeleteGroupLockoutAsync — only triggers for non-realm-superuser actors whose deletion
/// target is the last admin-capability source; the realm-superuser verify token bypasses it,
/// so API parity covers the happy path and the lockout substance is covered by unit tests
/// incl. the handler-wires-guard spy test).
/// Note verbatim: logged BEFORE removal so the audit trail retains the group data
/// (ActionLogBehavior writes the log in the same ambient transaction).
/// ILoggableCommand only.
/// </summary>
public record DeleteGroupCommand(Guid Id, Guid CurrentUserId, bool ActorIsRealmSuperUser)
    : IRequest<GroupResult>, ILoggableCommand<GroupResult>
{
    public ActionLogEntry? BuildLogEntry(GroupResult response)
    {
        if (!response.Success)
            return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.PermissionGroup,
            ItemId = Id,
            ActionType = ActionType.Delete,
            CreatedBy = CurrentUserId,
            CompanyId = null, // PermissionGroup has no CompanyId — company-independent log
            Note = $"Deleted group: {response.Name}"
        };
    }
}

public class DeleteGroupCommandHandler : IRequestHandler<DeleteGroupCommand, GroupResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IPermissionLockoutGuard _lockoutGuard;

    public DeleteGroupCommandHandler(IApplicationDbContext context, IPermissionLockoutGuard lockoutGuard)
    {
        _context = context;
        _lockoutGuard = lockoutGuard;
    }

    public async Task<GroupResult> Handle(DeleteGroupCommand request, CancellationToken cancellationToken)
    {
        var group = await _context.PermissionGroups.FindAsync(request.Id);
        if (group == null)
            return new GroupResult(false, "Group not found.", "NOT_FOUND");

        if (group.IsSystem)
            return new GroupResult(false, "System groups cannot be deleted.", "SYSTEM_GROUP_LOCKED");

        // Anti self-lockout: deleting a group that is the only remaining source of admin capability
        // (for any user, not just the actor) would leave the system unmanageable.
        if (await _lockoutGuard.WouldDeleteGroupLockoutAsync(request.CurrentUserId, request.Id, request.ActorIsRealmSuperUser))
            return new GroupResult(false,
                "Bạn không thể xóa nhóm này khi nó là nguồn cấp quyền quản trị duy nhất còn lại của hệ thống.",
                "SELF_LOCKOUT");

        _context.PermissionGroups.Remove(group);
        await _context.SaveChangesAsync(cancellationToken);

        return new GroupResult(true, "Group deleted successfully.", GroupId: request.Id, Name: group.Name);
    }
}
