namespace aspire_react.Server.Application.SystemInfos.Commands;

/// <summary>
/// Shared result for SystemInfo/SystemPosition commands. ErrorCode mirrors pre-migration
/// responses: NOT_FOUND → 404; null ErrorCode → 400 WITHOUT error_code (regex/empty/dup-code);
/// COMPANY_MISMATCH / FIELD_LOCKED / POSITION_IN_USE_BY_CHECKLIST / SYSTEM_IN_USE_BY_CAMPAIGN
/// → 400 WITH error_code. LogMeta/Note carry Update snapshots; CompanyId carries the resource's
/// company (SystemInfo IS company-scoped — logs are written with CompanyId = sys.CompanyId).
/// </summary>
public record SystemInfoResult(
    bool Success,
    string Message,
    string? ErrorCode = null,
    Guid? Id = null,
    string? Code = null,
    string? Name = null,
    Guid? CompanyId = null,
    string? LogMeta = null,
    string? Note = null);
