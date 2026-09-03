namespace aspire_react.Server.Application.Companies.Commands;

/// <summary>
/// Shared result for Company Create/Update/Delete commands. ErrorCode mirrors pre-migration
/// responses: NOT_FOUND → 404 (out-of-scope hide-existence / row missing); null ErrorCode →
/// 400 WITHOUT error_code (NOCO reserved, dup-code, circular-parent, has-children — old bodies
/// had none); COMPANY_IN_USE → 400 WITH error_code. LogMeta/Note carry the Update changes
/// snapshot (name/code/parentId) back to ActionLogBehavior.
/// </summary>
public record CompanyResult(
    bool Success,
    string Message,
    string? ErrorCode = null,
    Guid? CompanyId = null,
    string? Name = null,
    string? Code = null,
    Guid? ParentId = null,
    string? LogMeta = null,
    string? Note = null);
