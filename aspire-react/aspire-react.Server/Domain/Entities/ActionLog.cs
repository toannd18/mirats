using aspire_react.Server.Domain.Enums;

namespace aspire_react.Server.Domain.Entities;

public class ActionLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ItemType ItemType { get; set; }
    public Guid ItemId { get; set; }
    public AssignmentTargetType? TargetType { get; set; }
    public Guid? TargetId { get; set; }
    public ActionType ActionType { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid? LocationId { get; set; }
    public Guid? CompanyId { get; set; }
    public string? Note { get; set; }
    /// <summary>JSON with standardized Snipe-IT format: { changes: { field: { old, new } } }</summary>
    public string? LogMeta { get; set; }
    public DateTime ActionDate { get; set; } = DateTime.UtcNow;
    public string? RemoteIp { get; set; }
    public string? UserAgent { get; set; }
    public ActionSource ActionSource { get; set; } = ActionSource.Gui;
    /// <summary>Optional file attachment name (e.g. handover document)</summary>
    public string? FileName { get; set; }
    /// <summary>Optional file path on server</summary>
    public string? FilePath { get; set; }
    /// <summary>Soft delete timestamp — ActionLog is never hard-deleted</summary>
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // === Write-time snapshots for immutable audit trail ===
    /// <summary>Resolved location name at the time this action was logged.</summary>
    public string? LocationName { get; set; }
    /// <summary>Parent SystemInfo name when targeting a SystemPosition.</summary>
    public string? TargetSystemInfoName { get; set; }
    /// <summary>Parent SystemInfo id when targeting a SystemPosition — used to filter the system
    /// history hot path without relying on the name string (indexed).</summary>
    public Guid? TargetSystemInfoId { get; set; }

    // Navigation
    public User Creator { get; set; } = null!;
}