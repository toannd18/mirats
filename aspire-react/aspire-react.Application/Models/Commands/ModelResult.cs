namespace aspire_react.Server.Application.Models.Commands;

/// <summary>
/// Shared result for Model Create/Update/Delete commands. ErrorCode mirrors pre-migration
/// responses: NOT_FOUND → 404; null ErrorCode (has-Assets tree-style guard — message only) →
/// 400 WITHOUT error_code (old body had none). LogMeta/Note carry the Update changes-snapshot
/// (9 scalar fields incl. manufacturerId/categoryId/depreciationId as GUID old→new) back to
/// ActionLogBehavior via the command's BuildLogEntry.
/// </summary>
public record ModelResult(
    bool Success,
    string Message,
    string? ErrorCode = null,
    Guid? ModelId = null,
    string? Name = null,
    string? LogMeta = null,
    string? Note = null);
