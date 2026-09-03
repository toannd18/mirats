namespace aspire_react.Server.Application.Groups.Commands;

/// <summary>
/// Shared result for PermissionGroup commands. ErrorCode mirrors pre-migration responses:
/// NOT_FOUND → 404 "Group not found."; SYSTEM_GROUP_LOCKED / SELF_LOCKOUT → 400 with
/// <c>errorCode</c> in CAMELCASE (verbatim — Groups error bodies differ from the snake_case
/// error_code used by other controllers, see BACKLOG BUG-K convention note).
/// LogMeta/Note carry the Update changes snapshots back to ActionLogBehavior.
/// </summary>
public record GroupResult(
    bool Success,
    string Message,
    string? ErrorCode = null,
    Guid? GroupId = null,
    string? Name = null,
    bool IsSystem = false,
    string? LogMeta = null,
    string? Note = null);
