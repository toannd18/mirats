using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [Bước 0.1 / LL-3] Cổng an toàn cho các test Concurrency: chúng gọi API THẬT và TẠO FIXTURE trong
/// database của stack đang chạy (<c>MIRATS_TEST_BASE_URL</c>, mặc định http://localhost:5428) — nếu trỏ
/// nhầm vào DB dev/prod thì mỗi lần chạy sẽ ghi rác vào dữ liệu thật.
/// <para>
/// Vì vậy phải xác nhận rõ ràng bằng biến môi trường <c>MIRATS_TEST_THROWAWAY_DB=1</c>. Thiếu cờ →
/// mọi test Concurrency bị <b>SKIP kèm lý do</b> (xem <see cref="ThrowawayDbFactAttribute"/>), không
/// chạy ngầm.
/// </para>
/// </summary>
public static class ThrowawayDbGuard
{
    /// <summary>Tên biến môi trường xác nhận "stack đang chạy là DB TẠM, được phép ghi fixture".</summary>
    public const string FlagName = "MIRATS_TEST_THROWAWAY_DB";

    /// <summary>Chỉ true khi cờ được đặt đúng bằng "1".</summary>
    public static bool IsConfirmed =>
        string.Equals(Environment.GetEnvironmentVariable(FlagName)?.Trim(), "1", StringComparison.Ordinal);

    /// <summary>Lý do skip (hiện nguyên văn trong kết quả test).</summary>
    public static string SkipReason =>
        $"Test Concurrency ghi fixture vào database thật của stack đang chạy -> chỉ chạy khi xác nhận DB TẠM: " +
        $"đặt {FlagName}=1 (kèm MIRATS_TEST_BASE_URL trỏ vào stack tạm). Cờ không đặt => SKIP để không ghi vào DB dev.";
}

/// <summary>
/// <c>[Fact]</c> có điều kiện: tự SKIP (kèm lý do) khi chưa xác nhận DB tạm. xUnit v2 không có
/// <c>Assert.Skip</c> động, nên dùng cách chuẩn của v2: gán <see cref="FactAttribute.Skip"/> ngay trong
/// constructor của attribute — runner đọc giá trị này lúc discovery nên kết quả hiện "Skipped" + lý do.
/// </summary>
public sealed class ThrowawayDbFactAttribute : FactAttribute
{
    public ThrowawayDbFactAttribute()
    {
        if (!ThrowawayDbGuard.IsConfirmed)
            Skip = ThrowawayDbGuard.SkipReason;
    }
}
