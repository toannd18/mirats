namespace aspire_react.Server.Domain.Enums;

/// <summary>
/// Fixed lifecycle states for Asset. Replaces the dynamic StatusLabel FK.
/// </summary>
public enum AssetStatus
{
    /// <summary>Sẵn sàng — available for checkout</summary>
    Pending = 0,
    /// <summary>Đã cấp phát — currently checked out</summary>
    Deployed = 1,
    /// <summary>Đã lưu trữ — permanently retired</summary>
    Archived = 2
}