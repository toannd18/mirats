using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [Bước 0.1 / LL-3] Cổng an toàn cho các test Concurrency: chúng gọi API THẬT và TẠO FIXTURE trong
/// database của stack đang chạy (<c>MIRATS_TEST_BASE_URL</c>, mặc định http://localhost:5428) — nếu trỏ
/// nhầm vào DB dev/prod thì mỗi lần chạy sẽ ghi rác vào dữ liệu thật.
/// <para>
/// [DB-GUARD 2026-10-22] Cờ <c>MIRATS_TEST_THROWAWAY_DB=1</c> KHÔNG còn đủ. Connection string phải trỏ
/// tới database có tiền tố <c>mirats_tmp_</c> và không được trùng tên DB dev. Guard kiểm tra trực tiếp
/// tên database trong <c>MIRATS_TEST_POSTGRES</c>, không chỉ tin vào cờ môi trường.
/// </para>
/// </summary>
public static class ThrowawayDbGuard
{
    /// <summary>Tên biến môi trường xác nhận "stack đang chạy là DB TẠM, được phép ghi fixture".</summary>
    public const string FlagName = "MIRATS_TEST_THROWAWAY_DB";

    /// <summary>Tiền tố bắt buộc của database tạm.</summary>
    public const string TempDatabasePrefix = "mirats_tmp_";

    private static readonly HashSet<string> DevDatabaseNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "aspire_react",
        "aspire-react-db",
        "mirats",
        "mirats_dev",
        "postgres",
    };

    /// <summary>Chỉ true khi cờ được đặt đúng bằng "1".</summary>
    public static bool IsConfirmed =>
        string.Equals(Environment.GetEnvironmentVariable(FlagName)?.Trim(), "1", StringComparison.Ordinal);

    /// <summary>Đọc tên database từ connection string PostgreSQL (null nếu chuỗi hỏng/không có tên).</summary>
    public static string? GetDatabaseName(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        try
        {
            return new NpgsqlConnectionStringBuilder(connectionString).Database;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// True chỉ khi database là DB tạm: có tiền tố <c>mirats_tmp_</c> và không nằm trong danh sách
    /// database dev/prod đã biết. Không dùng cờ môi trường làm bằng chứng duy nhất.
    /// </summary>
    public static bool IsThrowawayConnectionString(string? connectionString)
    {
        var database = GetDatabaseName(connectionString);
        return !string.IsNullOrWhiteSpace(database)
            && database.StartsWith(TempDatabasePrefix, StringComparison.OrdinalIgnoreCase)
            && !DevDatabaseNames.Contains(database);
    }

    /// <summary>Ném lỗi để chặn test nếu connection string không phải database tạm.</summary>
    public static void EnsureThrowawayConnectionString(string? connectionString)
    {
        if (IsThrowawayConnectionString(connectionString)) return;

        var database = GetDatabaseName(connectionString) ?? "<không đọc được>";
        throw new InvalidOperationException(
            $"REFUSED: Test ghi fixture chỉ được chạy trên DB tạm có tiền tố '{TempDatabasePrefix}' " +
            $"và không trùng DB dev. Database nhận được: '{database}'. " +
            "Không chạy test này trên DB dev/prod.");
    }

    /// <summary>Guard dùng cho attribute: connection string hiện tại có phải DB tạm không.</summary>
    public static bool IsConfiguredThrowaway =>
        IsThrowawayConnectionString(Environment.GetEnvironmentVariable("MIRATS_TEST_POSTGRES"));

    /// <summary>Lý do skip (hiện nguyên văn trong kết quả test).</summary>
    public static string SkipReason =>
        $"Test Concurrency ghi fixture vào database thật của stack đang chạy -> chỉ chạy khi xác nhận DB TẠM: " +
        $"đặt {FlagName}=1 + MIRATS_TEST_POSTGRES trỏ database có tiền tố '{TempDatabasePrefix}' " +
        "(kèm MIRATS_TEST_BASE_URL trỏ vào stack tạm).";

    /// <summary>Lý do từ chối khi cờ đã bật nhưng connection string vẫn là DB dev/prod.</summary>
    public static string InvalidDatabaseSkipReason =>
        $"REFUSED: {FlagName}=1 nhưng MIRATS_TEST_POSTGRES không phải DB tạm. " +
        $"Database phải có tiền tố '{TempDatabasePrefix}' và không trùng DB dev.";
}

/// <summary>
/// <c>[Fact]</c> có điều kiện: tự SKIP (kèm lý do) khi chưa xác nhận DB tạm hoặc connection string
/// không phải DB tạm. xUnit v2 không có <c>Assert.Skip</c> động, nên gán <see cref="FactAttribute.Skip"/>
/// ngay trong constructor của attribute — runner đọc giá trị này lúc discovery.
/// </summary>
public sealed class ThrowawayDbFactAttribute : FactAttribute
{
    public ThrowawayDbFactAttribute()
    {
        if (!ThrowawayDbGuard.IsConfirmed)
            Skip = ThrowawayDbGuard.SkipReason;
        else if (!ThrowawayDbGuard.IsConfiguredThrowaway)
            Skip = ThrowawayDbGuard.InvalidDatabaseSkipReason;
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
        ThrowawayDbGuard.EnsureThrowawayConnectionString(connectionString);

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
