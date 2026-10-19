using Microsoft.EntityFrameworkCore;
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

/// <summary>
/// [GĐ1.2] Migration CHẠY ĐÚNG MỘT LẦN cho DB tạm.
/// <para>
/// Trước đây mỗi lớp test <c>[ThrowawayDbFact]</c> tự gọi <c>MigrateAsync()</c>. Trên DB TRẮNG, các lớp
/// chạy SONG SONG (xUnit) cùng tạo table/type ⇒ đụng nhau và FAIL giả — đo thật 2026-10-19: 9 FAIL khi
/// DB trắng, 0 FAIL sau khi migrate trước. Guard tĩnh theo connection string: luồng đầu migrate, các
/// luồng sau chờ rồi bỏ qua. Nhờ vậy nhóm ThrowawayDb chạy được trên DB trắng mà KHÔNG cần khởi động API.
/// </para>
/// </summary>
public static class ThrowawayDbMigrator
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly HashSet<string> Migrated = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Migrate một lần cho <paramref name="connectionString"/> (idempotent, thread-safe).</summary>
    public static async Task MigrateOnceAsync(
        aspire_react.Server.Infrastructure.Persistence.AppDbContext db, string connectionString, CancellationToken cancellationToken = default)
    {
        lock (Migrated)
        {
            if (Migrated.Contains(connectionString)) return;
        }

        await Gate.WaitAsync(cancellationToken);
        try
        {
            lock (Migrated)
            {
                if (Migrated.Contains(connectionString)) return;
            }

            await db.Database.MigrateAsync(cancellationToken);
            lock (Migrated)
            {
                Migrated.Add(connectionString);
            }
        }
        finally
        {
            Gate.Release();
        }
    }
}
