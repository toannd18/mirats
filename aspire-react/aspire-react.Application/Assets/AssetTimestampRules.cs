namespace aspire_react.Server.Application.Assets;

/// <summary>
/// [Bước 0.2 / LL-3] Quy tắc mốc thời gian cho các lệnh có ghi lý lịch lắp đặt (checkout/checkin).
/// <para>
/// Mốc thời gian do client gửi (<c>CheckoutAt</c>/<c>CheckinAt</c>) không được vượt quá hiện tại
/// <see cref="MaxFutureMinutes"/> phút: một khoảng lắp đặt bắt đầu ở tương lai làm lý lịch sai ngay khi
/// vừa ghi (và trước LL-2b có thể tạo ra HTTP 500 qua CHECK <c>EndedAt &gt;= StartedAt</c> khi tháo/lưu trữ
/// ngay sau đó). Mốc trong QUÁ KHỨ vẫn hợp lệ (nhập liệu bù), chỉ chặn tương lai.
/// </para>
/// <para>
/// Archive không có tham số mốc thời gian — handler luôn dùng <c>DateTime.UtcNow</c> — nên không có
/// gì để chặn ở đó.
/// </para>
/// </summary>
public static class AssetTimestampRules
{
    /// <summary>Error code trả về (HTTP 400) khi mốc thời gian ở tương lai quá cửa sổ cho phép.</summary>
    public const string Code = "TIMESTAMP_IN_FUTURE";

    /// <summary>Cửa sổ cho phép vượt hiện tại (phút) — hấp thụ lệch đồng hồ client/server nhỏ.</summary>
    public const int MaxFutureMinutes = 5;

    /// <summary>Message hiển thị cho người dùng.</summary>
    public const string Message =
        "Mốc thời gian vượt quá hiện tại quá 5 phút (mốc ở tương lai) — kiểm tra lại thời điểm.";

    /// <summary>
    /// True khi <paramref name="value"/> có giá trị và nằm SAU <paramref name="utcNow"/> +
    /// <see cref="MaxFutureMinutes"/> phút (so sánh nghiêm ngặt: đúng biên vẫn hợp lệ).
    /// </summary>
    public static bool IsBeyondFutureWindow(DateTime? value, DateTime utcNow) =>
        value.HasValue && value.Value > utcNow.AddMinutes(MaxFutureMinutes);
}
