namespace aspire_react.Server.Application.Suppliers.Commands;

/// <summary>
/// Shared result for Supplier Create/Update/Delete commands. ErrorCode mirrors pre-migration
/// responses: NOT_FOUND → 404; null ErrorCode (Code length / dup Code / dup Name rules) → 400
/// WITHOUT error_code (old bodies had none); SUPPLIER_IN_USE → 400 with error_code.
/// LogMeta/Note carry the Update changes-snapshot back to ActionLogBehavior.
/// </summary>
public record SupplierResult(
    bool Success,
    string Message,
    string? ErrorCode = null,
    Guid? SupplierId = null,
    string? Code = null,
    string? Name = null,
    string? LogMeta = null,
    string? Note = null);
