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
/// SYSTEM_GROUP_LOCKED verbatim (IsSystem groups cannot be renamed). Name/Description assigned
/// unconditionally (full-put — verbatim). LogMeta ×2 (name/description old→new).
/// [BUG-K FIX 2026-09-05] Validation ADDED after the existing guards (behavior change approved):
/// empty-Name → 400 "Group name is required."; duplicate-Name on rename (CASE-INSENSITIVE, only
/// when the name actually CHANGES, excluding self) → 400 "A group with this name already exists."
/// ILoggableCommand only.
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

        // [BUG-K FIX] empty-name + dup-name (case-insensitive, only when actually changed).
        if (string.IsNullOrWhiteSpace(request.Name))
            return new GroupResult(false, "Group name is required.");
        if (request.Name != group.Name)
        {
            var dup = await _context.PermissionGroups.AnyAsync(
                g => g.Id != request.Id && g.Name.ToLower() == request.Name.ToLower(), cancellationToken);
            if (dup)
                return new GroupResult(false, "A group with this name already exists.");
        }

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
