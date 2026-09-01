namespace aspire_react.Server.Application.Departments.Commands;

/// <summary>
/// Shared result for Department Create/Update/Delete commands. ErrorCode drives the controller's
/// HTTP mapping and mirrors the pre-migration responses EXACTLY:
/// - NOT_FOUND → 404 {status, message} (no error_code — old body had none)
/// - COMPANY_MISMATCH / DEPARTMENT_IN_USE → 400 {status, message, error_code}
/// - null ErrorCode (name empty / duplicate name) → 400 {status, message} (no error_code — old body had none)
/// LogMeta/Note are carried back to ActionLogBehavior via the command's BuildLogEntry for UPDATE
/// (the before/after snapshot lives in the handler where the tracked entity is).
/// </summary>
public record DepartmentResult(
    bool Success,
    string Message,
    string? ErrorCode = null,
    Guid? DepartmentId = null,
    string? Name = null,
    Guid? CompanyId = null,
    string? LogMeta = null,
    string? Note = null);
