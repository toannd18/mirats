using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;

namespace aspire_react.Server.Domain.Interfaces;

public interface IActionLogService
{
    void LogAction(
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
        string? fileName = null);

    /// <summary>
    /// Stages a typed <see cref="ActionLogEntry"/> into the change tracker (Task S2a). Unlike
    /// <see cref="LogAction"/>, this does NO enrichment (CreatedBy/SystemInfo/LocationName/RemoteIp/
    /// UserAgent/ActionSource) — it persists exactly what the entry describes. This is the
    /// typed-safe replacement for the free-form <c>ActionLog</c> object-initializer; the caller's
    /// <c>SaveChanges</c> persists it in the same transaction as the domain change.
    /// </summary>
    void Log(ActionLogEntry entry);

    Task<Guid> GetCurrentUserIdAsync();
}