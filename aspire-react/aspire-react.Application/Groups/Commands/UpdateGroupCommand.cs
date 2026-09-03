using System.Text.Json;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;

namespace aspire_react.Server.Application.Groups.Commands;

/// <summary>
/// [Giai đoạn 3] PUT /api/v1/groups/{id} (extracted from GroupsController.Update).
/// SYSTEM_GROUP_LOCKED verbatim (IsSystem groups cannot be renamed). Name/Description assigned
/// unconditionally (full-put — verbatim). LogMeta ×2 (name/description old→new).
/// ILoggableCommand only. TODO BUG-K: no duplicate-Name check on rename (verbatim).
/// </summary>
public record UpdateGroupCommand(Guid Id, string Name, string? Description, Guid CurrentUserId)
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

        var oldName = group.Name;
        var oldDescription = group.Description;
        group.Name = request.Name;
        group.Description = request.Description;

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
