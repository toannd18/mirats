using System.Text.Json;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Groups.Commands;

/// <summary>
/// [Giai đoạn 3] PUT /api/v1/groups/{id} (extracted from GroupsController.Update).
/// SYSTEM_GROUP_LOCKED verbatim (IsSystem groups cannot be renamed).
/// [FIX-N4 2026-10-02] PATCH semantics (Task M1/M2): Name absent → keep (blank-when-sent is still a
/// 400); Description absent → keep. Before this fix Description was assigned unconditionally, so a
/// partial payload (e.g. rename-only) silently CLEARED the description.
/// [BUG-K FIX 2026-09-05] Validation ADDED after the existing guards: empty-Name → 400
/// "Group name is required."; duplicate-Name on rename (CASE-INSENSITIVE, only when the name actually
/// CHANGES, excluding self) → 400 "A group with this name already exists."
/// ILoggableCommand only.
/// </summary>
public record UpdateGroupCommand(Guid Id, string? Name, string? Description, Guid CurrentUserId)
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

public class UpdateGroupCommandHandler : IRequestHandler<UpdateGroupCommand, GroupResult>
{
    private readonly IApplicationDbContext _context;

    public UpdateGroupCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<GroupResult> Handle(UpdateGroupCommand request, CancellationToken cancellationToken)
    {
        var group = await _context.PermissionGroups.FindAsync(request.Id);
        if (group == null)
            return new GroupResult(false, "Group not found.", "NOT_FOUND");

        if (group.IsSystem)
            return new GroupResult(false, "System groups cannot be renamed.", "SYSTEM_GROUP_LOCKED");

        // [FIX-N4] Patch semantics: Name absent → keep (blank-when-sent still 400, BUG-K rule);
        // dup-name check only when the name actually changes (case-insensitive, excluding self).
        var newName = group.Name;
        if (request.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return new GroupResult(false, "Group name is required.");
            newName = request.Name;
            if (newName != group.Name)
            {
                var dup = await _context.PermissionGroups.AnyAsync(
                    g => g.Id != request.Id && g.Name.ToLower() == newName.ToLower(), cancellationToken);
                if (dup)
                    return new GroupResult(false, "A group with this name already exists.");
            }
        }

        var oldName = group.Name;
        var oldDescription = group.Description;
        group.Name = newName;
        // [FIX-N4] Description absent → keep the stored value (was cleared unconditionally).
        if (request.Description is not null) group.Description = request.Description;

        var logMeta = JsonSerializer.Serialize(new
        {
            changes = new
            {
                name = new { old = oldName, @new = group.Name },
                description = new { old = oldDescription, @new = group.Description }
            }
        });

        return new GroupResult(true, "Group updated successfully.",
            GroupId: group.Id, Name: group.Name,
            LogMeta: logMeta, Note: "Group updated.");
    }
}
