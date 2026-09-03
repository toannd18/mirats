using System.Text.Json;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Groups.Commands;

public record GroupPermissionEntry(string PermissionKey, PermissionValue Value);

/// <summary>
/// [Giai đoạn 3] PUT /api/v1/groups/{id}/permissions (extracted from
/// GroupsController.UpdateGroupPermissions). FULL-REPLACE semantics verbatim: ALL existing
/// GroupPermissions removed, then the submitted set re-added.
/// Guards verbatim: NOT_FOUND → SELF_LOCKOUT via IPermissionLockoutGuard
/// (WouldGroupPermissionEditLockoutAsync — computed for every member of the group with the NEW
/// permission set; realm superuser bypasses). LogMeta: permissions old[] (int values) vs new[]
/// (verbatim — the old controller serialized the raw enum → number).
/// ILoggableCommand only.
/// </summary>
public record UpdateGroupPermissionsCommand(
    Guid Id,
    IReadOnlyList<GroupPermissionEntry> Permissions,
    Guid CurrentUserId,
    bool ActorIsRealmSuperUser)
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
            ActionType = ActionType.Update,
            CreatedBy = CurrentUserId,
            CompanyId = null, // PermissionGroup has no CompanyId — company-independent log
            LogMeta = response.LogMeta,
            Note = response.Note
        };
    }
}

public class UpdateGroupPermissionsCommandHandler : IRequestHandler<UpdateGroupPermissionsCommand, GroupResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IPermissionLockoutGuard _lockoutGuard;

    public UpdateGroupPermissionsCommandHandler(IApplicationDbContext context, IPermissionLockoutGuard lockoutGuard)
    {
        _context = context;
        _lockoutGuard = lockoutGuard;
    }

    public async Task<GroupResult> Handle(UpdateGroupPermissionsCommand request, CancellationToken cancellationToken)
    {
        var group = await _context.PermissionGroups
            .Include(g => g.GroupPermissions)
            .FirstOrDefaultAsync(g => g.Id == request.Id, cancellationToken);

        if (group == null)
            return new GroupResult(false, "Group not found.", "NOT_FOUND");

        var drafts = request.Permissions
            .Select(p => new GroupPermissionDraft(p.PermissionKey, p.Value))
            .ToList();

        // Anti self-lockout: removing the management permissions (admin/users.edit) from the
        // last group that grants them to the acting admin would leave the system unmanageable.
        if (await _lockoutGuard.WouldGroupPermissionEditLockoutAsync(request.CurrentUserId, request.Id, drafts, request.ActorIsRealmSuperUser))
            return new GroupResult(false,
                "Bạn không thể gỡ quyền quản trị khỏi nhóm này khi bạn là người cuối cùng còn giữ quyền quản trị.",
                "SELF_LOCKOUT");

        var oldPermissions = group.GroupPermissions
            .Select(p => new { p.PermissionKey, Value = (int)p.Value })
            .ToList();

        _context.GroupPermissions.RemoveRange(group.GroupPermissions);

        foreach (var perm in request.Permissions)
        {
            _context.GroupPermissions.Add(new GroupPermission
            {
                GroupId = request.Id,
                PermissionKey = perm.PermissionKey,
                Value = perm.Value
            });
        }

        var logMeta = JsonSerializer.Serialize(new
        {
            changes = new
            {
                permissions = new
                {
                    old = oldPermissions,
                    @new = request.Permissions.Select(p => new { p.PermissionKey, p.Value })
                }
            }
        });

        await _context.SaveChangesAsync(cancellationToken);

        return new GroupResult(true, "Group permissions updated.",
            GroupId: group.Id, Name: group.Name,
            LogMeta: logMeta, Note: $"Group permissions updated: {group.Name}");
    }
}
