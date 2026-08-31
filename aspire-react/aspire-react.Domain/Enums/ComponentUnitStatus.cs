namespace aspire_react.Server.Domain.Enums;

/// <summary>Lifecycle status of a single serial-tracked ComponentUnit.</summary>
public enum ComponentUnitStatus
{
    InStock = 0,
    Allocated = 1,
    Damaged = 2,
    Disposed = 3
}
