namespace aspire_react.Server.Application.Manufacturers.Commands;

/// <summary>
/// Shared result for Manufacturer Create/Update/Delete commands. ErrorCode mirrors
/// pre-migration responses: NOT_FOUND → 404; null ErrorCode (Code length / dup Code / dup Name
/// rules) → 400 WITHOUT error_code (old bodies had none); MANUFACTURER_IN_USE → 400 with
/// error_code. LogMeta/Note carry the Update changes-snapshot back to ActionLogBehavior.
/// </summary>
public record ManufacturerResult(
    bool Success,
    string Message,
    string? ErrorCode = null,
    Guid? ManufacturerId = null,
    string? Code = null,
    string? Name = null,
    string? LogMeta = null,
    string? Note = null);
