namespace aspire_react.Server.Domain.Enums;

/// <summary>
/// License seat checkout targets — User / Asset / SystemInfo (the "Hệ thống" target is the SystemInfo
/// PARENT, never a SystemPosition child — a license applies to the whole system).
/// </summary>
public enum LicenseSeatTargetType
{
    User = 1,
    Asset = 2,
    SystemInfo = 3
}