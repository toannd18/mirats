namespace aspire_react.Server.Domain.Enums;

/// <summary>[FEAT-SPARE 3] Direction of a spare-part movement for one system.</summary>
public enum SparePartMovementDirection
{
    /// <summary>Cấp vật tư dự phòng cho hệ thống — giảm tồn kho.</summary>
    Issue = 1,
    /// <summary>Nhận lại vật tư dự phòng từ hệ thống — tăng tồn kho.</summary>
    Return = 2
}
