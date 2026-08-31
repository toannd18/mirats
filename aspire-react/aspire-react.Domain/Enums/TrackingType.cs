namespace aspire_react.Server.Domain.Enums;

/// <summary>How a Component's stock is tracked.</summary>
public enum TrackingType
{
    /// <summary>Counted as a single quantity pool (default — backward-compatible with existing data).</summary>
    Bulk = 0,
    /// <summary>Each physical unit is tracked individually by serial number (ComponentUnit rows).</summary>
    Serial = 1
}
