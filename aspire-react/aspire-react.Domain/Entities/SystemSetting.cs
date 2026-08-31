namespace aspire_react.Server.Domain.Entities;

/// <summary>
/// Shared key-value store for system-wide configuration (Task ASSET-TAG-AUTO). Unlike operational
/// counters (see <see cref="AssetTagCounter"/>), this holds STATIC config that is read often and
/// written rarely (e.g. the Asset Tag format template). Keys are stable string identifiers.
/// </summary>
public class SystemSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
