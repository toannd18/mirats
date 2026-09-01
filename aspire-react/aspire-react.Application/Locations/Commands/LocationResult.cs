namespace aspire_react.Server.Application.Locations.Commands;

/// <summary>
/// Shared result for Location Create/Update/Delete commands. ErrorCode mirrors pre-migration
/// responses: NOT_FOUND → 404; null ErrorCode (has-children tree guard) → 400 WITHOUT error_code
/// (old body had none); LOCATION_IN_USE → 400 with error_code. LogMeta/Note carry the Update
/// changes-snapshot back to ActionLogBehavior (before-values live in the handler).
/// </summary>
public record LocationResult(
    bool Success,
    string Message,
    string? ErrorCode = null,
    Guid? LocationId = null,
    string? Name = null,
    Guid? CompanyId = null,
    string? LogMeta = null,
    string? Note = null);
