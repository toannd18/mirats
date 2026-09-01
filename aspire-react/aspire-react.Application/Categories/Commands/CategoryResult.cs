namespace aspire_react.Server.Application.Categories.Commands;

/// <summary>
/// Shared result for Category Create/Update/Delete commands. ErrorCode mirrors the pre-migration
/// responses: NOT_FOUND → 404; CATEGORY_IN_USE → 400 with error_code; null ErrorCode → 400
/// without error_code (old bodies carried no error_code key). LogMeta/Note carry the Update
/// changes-snapshot back to ActionLogBehavior (before-values live in the handler).
/// </summary>
public record CategoryResult(
    bool Success,
    string Message,
    string? ErrorCode = null,
    Guid? CategoryId = null,
    string? Name = null,
    string? LogMeta = null,
    string? Note = null);
