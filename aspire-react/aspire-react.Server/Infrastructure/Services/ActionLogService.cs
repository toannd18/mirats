using System.Security.Claims;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Infrastructure.Services;

public class ActionLogService : Domain.Interfaces.IActionLogService
{
    private readonly AppDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ActionLogService(AppDbContext context, IHttpContextAccessor httpContextAccessor)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
    }

    public void LogAction(
        ItemType itemType,
        Guid itemId,
        ActionType actionType,
        Guid? loggedByUserId = null,
        AssignmentTargetType? targetType = null,
        Guid? targetId = null,
        string? note = null,
        string? logMeta = null,
        Guid? locationId = null,
        Guid? companyId = null,
        string? fileName = null)
    {
        var createdBy = Guid.Empty;

        if (loggedByUserId.HasValue && loggedByUserId.Value != Guid.Empty)
        {
            createdBy = loggedByUserId.Value;
        }
        else
        {
            // [SEC-FIX CLAIM-CLEANUP, 2026-08-23] ONLY "local_user_id" (JIT-stamped) is a user
            // identity source — Keycloak sub/preferred_username are never used (bug-class 1).
            // Absent claim → Guid.Empty (fail closed; the caller's explicit loggedByUserId still
            // wins above, and a Guid.Empty createdBy skips logging rather than misattributing).
            if (Guid.TryParse(GetClaimValue("local_user_id"), out var localId) && localId != Guid.Empty)
            {
                createdBy = localId;
            }
        }

        if (createdBy == Guid.Empty)
            return;

        // ---- Write-time snapshot: resolve the parent SystemInfo (id + name) for SystemPosition
        // targets in ONE query so TargetSystemInfoId and TargetSystemInfoName are captured together
        // and consistently at the moment the action is logged (mirrors LocationId/LocationName). ----
        Guid? targetSystemInfoId = null;
        string? targetSystemInfoName = null;
        if (targetType == AssignmentTargetType.SystemPosition && targetId.HasValue)
        {
            var sysInfo = _context.SystemPositions
                .Where(sp => sp.Id == targetId.Value)
                .Select(sp => new { sp.SystemInfo.Id, sp.SystemInfo.Name })
                .FirstOrDefault();
            targetSystemInfoId = sysInfo?.Id;
            targetSystemInfoName = sysInfo?.Name;
        }
        else if (targetType == AssignmentTargetType.SystemInfo && targetId.HasValue)
        {
            // License checkout targets the SystemInfo PARENT directly (never a SystemPosition child).
            var sysInfo = _context.SystemInfos
                .Where(si => si.Id == targetId.Value)
                .Select(si => new { si.Id, si.Name })
                .FirstOrDefault();
            targetSystemInfoId = sysInfo?.Id;
            targetSystemInfoName = sysInfo?.Name;
        }

        var log = new ActionLog
        {
            ItemType = itemType,
            ItemId = itemId,
            ActionType = actionType,
            TargetType = targetType,
            TargetId = targetId,
            CreatedBy = createdBy,
            LocationId = locationId,
            CompanyId = companyId,
            Note = note,
            LogMeta = logMeta,
            FileName = fileName,
            RemoteIp = GetRemoteIp(),
            UserAgent = GetUserAgent(),
            ActionSource = DetectSource(),
            ActionDate = DateTime.UtcNow,
            // ---- Write-time snapshot: resolve location and SystemInfo names once, immutably ----
            LocationName = locationId.HasValue
                ? _context.Locations.Where(l => l.Id == locationId.Value).Select(l => l.Name).FirstOrDefault()
                : null,
            TargetSystemInfoId = targetSystemInfoId,
            TargetSystemInfoName = targetSystemInfoName
        };

        _context.ActionLogs.Add(log);
    }

    public void Log(ActionLogEntry entry)
    {
        // Typed, compile-safe staging (Task S2a): persists exactly what the entry describes, in the
        // caller's transaction. No enrichment (unlike LogAction) — behavior identical to the old
        // free-form ActionLog object-initializer call sites this replaces.
        _context.ActionLogs.Add(entry.Build());
    }

    public Task<Guid> GetCurrentUserIdAsync()
    {
        // [SEC-FIX CLAIM-CLEANUP, 2026-08-23] ONLY "local_user_id" (JIT-stamped) is a user identity
        // source — Keycloak sub/preferred_username are never used (bug-class 1). Absent → Guid.Empty.
        if (Guid.TryParse(GetClaimValue("local_user_id"), out var localId) && localId != Guid.Empty)
            return Task.FromResult(localId);
        return Task.FromResult(Guid.Empty);
    }

    private string? GetClaimValue(string claimType)
    {
        return _httpContextAccessor.HttpContext?.User?.FindFirstValue(claimType);
    }

    private string? GetRemoteIp()
    {
        var context = _httpContextAccessor.HttpContext;
        return context?.Connection?.RemoteIpAddress?.ToString();
    }

    private string? GetUserAgent()
    {
        return _httpContextAccessor.HttpContext?.Request?.Headers.UserAgent.ToString();
    }

    private ActionSource DetectSource()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context == null) return ActionSource.Cli;

        if (context.Request.Headers.ContainsKey("X-CSRF-TOKEN"))
            return ActionSource.Gui;

        var authHeader = context.Request.Headers.Authorization.ToString();
        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return ActionSource.Api;

        return ActionSource.Cli;
    }
}