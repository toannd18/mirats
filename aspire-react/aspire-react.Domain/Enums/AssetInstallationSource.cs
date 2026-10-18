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
    Backfill = 3,
    /// <summary>
    /// [LL-5] Bản ghi do thao tác ĐÍNH CHÍNH tạo ra (người dùng xác nhận lại mốc thời gian/vị trí).
    /// Khác <see cref="Api"/> ở chỗ bản này là bản thay thế của một bản trước (<c>CorrectsEntryId != null</c>);
    /// mốc thời gian của bản đính chính LUÔN là mốc do người dùng xác nhận ⇒ không còn mang nhãn ước tính.
    /// </summary>
    Correction = 4
}
