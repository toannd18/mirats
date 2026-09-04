using System.Text.Json;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Users.DTOs;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Users.Commands;

public record UpdateUserGroupsResult(
    bool Success,
    string Message,
    string? ErrorCode = null,
    Guid? UserId = null,
    string? Username = null,
    IReadOnlyList<UserGroupDto>? Groups = null,
    Guid? CompanyId = null,
    string? LogMeta = null,
    string? Note = null);

/// <summary>
/// [Giai đoạn 3] PUT /api/v1/users/{id}/groups (extracted verbatim from
/// UsersController.UpdateUserGroups). FULL-REPLACE semantics verbatim: ALL existing UserGroups
/// removed, then the submitted set re-added. Guards verbatim: NOT_FOUND (404 hide-existence,
/// incl. company-scope 404) → GROUP_NOT_FOUND (400, errorCode CAMELCASE verbatim) →
/// SELF_LOCKOUT via IPermissionLockoutGuard (WouldSelfAssignLockoutAsync with the realm-superuser
/// flag resolved in the controller — handlers cannot read HttpContext). LogMeta: groupIds
/// old[] vs new[] (verbatim). ILoggableCommand only (no output-cache on users endpoints).
/// Error-shape parity note: this endpoint uses errorCode CAMELCASE (not error_code) — verbatim.
/// </summary>
public record UpdateUserGroupsCommand(
    Guid Id,
    IReadOnlyList<Guid> GroupIds,
    Guid CurrentUserId,
    bool ActorIsRealmSuperUser)
    : IRequest<UpdateUserGroupsResult>, ILoggableCommand<UpdateUserGroupsResult>
{
    public ActionLogEntry? BuildLogEntry(UpdateUserGroupsResult response)
    {
        if (!response.Success)
            return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.User,
            ItemId = Id,
            ActionType = ActionType.Update,
            CreatedBy = CurrentUserId,
            CompanyId = response.CompanyId,
            LogMeta = response.LogMeta,
            Note = response.Note
        };
    }
}

public class UpdateUserGroupsCommandHandler : IRequestHandler<UpdateUserGroupsCommand, UpdateUserGroupsResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IPermissionLockoutGuard _lockoutGuard;

    public UpdateUserGroupsCommandHandler(
        IApplicationDbContext context,
        ICompanyScopeService companyScope,
        IPermissionLockoutGuard lockoutGuard)
    {
        _context = context;
        _companyScope = companyScope;
        _lockoutGuard = lockoutGuard;
    }

    public async Task<UpdateUserGroupsResult> Handle(UpdateUserGroupsCommand request, CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken);
        if (user == null)
            return new UpdateUserGroupsResult(false, "User not found.", "NOT_FOUND");

        // [SEC-FIX CS-3] verbatim: company admin may only manage groups of users in their own
        // company (or floater); superuser (scope → null) unrestricted. Out-of-scope → 404.
        var actorCompanyScope = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (actorCompanyScope.HasValue && user.CompanyId.HasValue && user.CompanyId.Value != actorCompanyScope.Value)
            return new UpdateUserGroupsResult(false, "User not found.", "NOT_FOUND");

        var requestedIds = (request.GroupIds ?? new List<Guid>()).Distinct().ToList();
        var validGroupIds = await _context.PermissionGroups
            .Where(g => requestedIds.Contains(g.Id))
            .Select(g => g.Id)
            .ToListAsync(cancellationToken);

        if (validGroupIds.Count != requestedIds.Count)
            return new UpdateUserGroupsResult(false, "One or more groups do not exist.", "GROUP_NOT_FOUND");

        // Anti self-lockout verbatim (Vietnamese message + errorCode camelCase).
        if (await _lockoutGuard.WouldSelfAssignLockoutAsync(request.CurrentUserId, request.Id, validGroupIds, request.ActorIsRealmSuperUser))
            return new UpdateUserGroupsResult(false,
                "Bạn không thể tự gỡ quyền quản trị của chính mình khi bạn là người cuối cùng còn giữ quyền quản trị.",
                "SELF_LOCKOUT");

        // Capture old assignment for the audit trail.
        var oldGroupIds = await _context.UserGroups
            .Where(ug => ug.UserId == request.Id)
            .Select(ug => ug.GroupId)
            .ToListAsync(cancellationToken);

        _context.UserGroups.RemoveRange(_context.UserGroups.Where(ug => ug.UserId == request.Id));
        foreach (var groupId in validGroupIds)
        {
            _context.UserGroups.Add(new UserGroup { UserId = request.Id, GroupId = groupId });
        }

        var logMeta = JsonSerializer.Serialize(new
        {
            changes = new
            {
                groupIds = new
                {
                    old = oldGroupIds,
                    @new = validGroupIds
                }
            }
        });
        const string note = "User group assignments updated.";

        await _context.SaveChangesAsync(cancellationToken);

        var groups = await _context.PermissionGroups
            .Where(g => validGroupIds.Contains(g.Id))
            .Select(g => new UserGroupDto(g.Id, g.Name, g.IsSystem))
            .ToListAsync(cancellationToken);

        return new UpdateUserGroupsResult(true, "User groups updated.",
            UserId: user.Id, Username: user.Username, Groups: groups,
            CompanyId: user.CompanyId, LogMeta: logMeta, Note: note);
    }
}
