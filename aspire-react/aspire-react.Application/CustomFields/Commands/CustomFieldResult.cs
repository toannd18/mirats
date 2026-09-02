namespace aspire_react.Server.Application.CustomFields.Commands;

/// <summary>
/// Shared result for CustomField Create/Update/Delete commands. ErrorCode mirrors pre-migration
/// responses: NOT_FOUND → 404; null ErrorCode (dup-slug rule / CUSTOM_FIELD_IN_USE-style bodies)
/// → 400 bodies per-action (dup-slug Create: no error_code; guard: error_code CUSTOM_FIELD_IN_USE).
/// LogMeta/Note carry the Update changes-snapshot (8 fields) back to ActionLogBehavior.
/// </summary>
public record CustomFieldResult(
    bool Success,
    string Message,
    string? ErrorCode = null,
    Guid? CustomFieldId = null,
    string? Name = null,
    string? LogMeta = null,
    string? Note = null);
