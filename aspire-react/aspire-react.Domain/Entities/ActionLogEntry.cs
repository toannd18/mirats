using aspire_react.Server.Domain.Enums;

namespace aspire_react.Server.Domain.Entities;

/// <summary>
/// Typed, compile-safe builder for an <see cref="ActionLog"/> (Task S2a).
/// <para>
/// Replaces the free-form, untrusted <c>ActionLog</c> object-initializer
/// (callers could silently omit a mandatory field, causing the real bugs fixed in Task N / ST4 /
/// Task E: missing CompanyId, wrong TargetType, null TargetId). The 5 fields present in EVERY
/// ActionLog write are declared <c>required</c>, so the compiler refuses to build an entry that
/// omits one. Action-specific fields (TargetType/TargetId/LogMeta/Note) remain optional.
/// </para>
/// <para>
/// Deliberate choice (for reviewers): <c>CompanyId</c> is <c>required Guid?</c> (nullable) and is NOT
/// runtime-rejected for <see cref="Guid.Empty"/>. Reason: a floater Maintenance record legitimately
/// carries <c>CompanyId == Guid.Empty</c> (server sets it to <c>Asset.CompanyId ?? Guid.Empty</c>),
/// so rejecting Guid.Empty would break legitimate floater logging. <c>required</c> still forces the
/// field to be EXPLICITLY assigned at every call site — the goal is to prevent "forgot to pass
/// CompanyId", not to forbid the valid floater sentinel.
/// </para>
/// </summary>
public sealed class ActionLogEntry
{
    // ── Mandatory (compile-enforced) — present in every ActionLog write ──
    public required ItemType ItemType { get; init; }
    public required Guid ItemId { get; init; }
    public required ActionType ActionType { get; init; }
    public required Guid CreatedBy { get; init; }
    public required Guid? CompanyId { get; init; }

    // ── Action-specific (optional) ──
    public AssignmentTargetType? TargetType { get; init; }
    public Guid? TargetId { get; init; }
    public Guid? TargetSystemInfoId { get; init; }
    public string? TargetSystemInfoName { get; init; }
    public string? Note { get; init; }
    public string? LogMeta { get; init; }
    public Guid? LocationId { get; init; }
    public string? FileName { get; init; }
    /// <summary>Optional explicit log timestamp (Task S2b — used by Asset Audit/Accept/Decline which
    /// record a specific date). When null, <see cref="ActionLog.ActionDate"/> keeps its entity default (UtcNow).</summary>
    public DateTime? ActionDate { get; init; }

    /// <summary>Materializes the typed entry into an <see cref="ActionLog"/> entity for persistence.</summary>
    public ActionLog Build() => new()
    {
        ItemType = ItemType,
        ItemId = ItemId,
        ActionType = ActionType,
        TargetType = TargetType,
        TargetId = TargetId,
        CreatedBy = CreatedBy,
        LocationId = LocationId,
        CompanyId = CompanyId,
        Note = Note,
        LogMeta = LogMeta,
        TargetSystemInfoId = TargetSystemInfoId,
        TargetSystemInfoName = TargetSystemInfoName,
        FileName = FileName,
        ActionDate = ActionDate ?? DateTime.UtcNow
    };
}
