namespace aspire_react.Server.Domain.Enums;

/// <summary>
/// [LL-1] Nguồn phát sinh một bản ghi <c>AssetInstallation</c>. Enum serialize dạng string
/// (global JsonStringEnumConverter), lưu DB dạng int.
/// </summary>
public enum AssetInstallationSource
{
    /// <summary>Thao tác qua API (checkout/checkin/điều chuyển) — luồng chuẩn.</summary>
    Api = 1,

    /// <summary>Nhập từ dữ liệu ngoài (import).</summary>
    Import = 2,

    /// <summary>Backfill hệ thống khi áp dụng tính năng (không có người ghi).</summary>
    Backfill = 3
}
