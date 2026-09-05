using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Groups.Commands;

/// <summary>
/// [Giai đoạn 3] POST /api/v1/groups (extracted from GroupsController.Create).
/// [BUG-K FIX 2026-09-05] Validation ADDED (behavior change approved): empty-Name → 400 "Group
/// name is required."; duplicate-Name (CASE-INSENSITIVE — decided at fix time per the sketch:
/// group names act as role-like identifiers, "Admin"/"admin" duplication is the same confusion)
/// → 400 "A group with this name already exists." (no errorCode, soft-fail section style).
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
        // [BUG-K FIX] empty-name + dup-name (case-insensitive) before any mutation.
        if (string.IsNullOrWhiteSpace(request.Name))
            return new GroupResult(false, "Group name is required.");
        var dup = await _context.PermissionGroups.AnyAsync(
            g => g.Name.ToLower() == request.Name.ToLower(), cancellationToken);
        if (dup)
            return new GroupResult(false, "A group with this name already exists.");

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
