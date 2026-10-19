using System.Text.Json;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [MT-TZ] Canary cho quy tắc Kind của các cột <c>timestamp without time zone</c> (naive).
/// <para>
/// Bối cảnh: cột naive chứa WALL-CLOCK UTC. Npgsql đọc ra <see cref="DateTimeKind.Unspecified"/> ⇒
/// System.Text.Json BỎ hậu tố "Z" ⇒ FE <c>new Date(value)</c> hiểu là giờ ĐỊA PHƯƠNG ⇒ lệch 7h và lệch
/// 1 NGÀY với mốc nửa đêm giờ VN. <see cref="AppDbContext"/> đăng ký ValueConverter TƯỜNG MINH cho TỪNG
/// property (đọc → Kind=Utc, ghi → Kind=Unspecified); test này chặn việc quên đăng ký khi thêm cột naive.
/// </para>
/// <para>
/// Test 1 chạy trên model (InMemory, không cần DB). Test 2/3 đối chiếu với Postgres THẬT
/// (<see cref="ThrowawayDbFactAttribute"/>: cần <c>MIRATS_TEST_THROWAWAY_DB=1</c> + <c>MIRATS_TEST_POSTGRES</c>
/// trỏ vào DB TẠM trong container Postgres RIÊNG — xem workflow §3.16).
/// </para>
/// </summary>
public class NaiveDateTimeKindTests
{
    public const string ConnectionStringEnv = "MIRATS_TEST_POSTGRES";

    private const string NaiveStoreType = "timestamp without time zone";

    /// <summary>Số cột naive đã kiểm kê (MT-TZ Bước 1, khớp <c>docs/HANDOFF_DATETIME_KIND_AUDIT.md</c> §7).</summary>
    private const int ExpectedNaiveColumnCount = 16;

    /// <summary>Giá trị "wall-clock UTC" dùng làm mẫu — CỐ TÌNH Kind=Unspecified (đúng như Npgsql trả về).</summary>
    private static readonly DateTime Probe = new(2026, 3, 4, 5, 6, 7, DateTimeKind.Unspecified);

    [Fact]
    public void Model_naive_DateTime_properties_must_convert_read_to_Utc()
    {
        using var db = TestHelpers.CreateContext(Guid.NewGuid().ToString());

        var naive = NaiveProperties(db).ToList();
        var missing = naive.Where(kv => !ConvertsReadToUtc(kv.Value)).Select(kv => kv.Key).ToList();

        Assert.Equal(ExpectedNaiveColumnCount, naive.Count);
        Assert.Empty(missing);
    }

    [ThrowawayDbFact]
    public async Task Postgres_naive_columns_match_model_and_are_all_registered()
    {
        await using var db = await CreatePostgresContextAsync();

        var modelNaive = NaiveProperties(db).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        var dbNaive = await db.Database.SqlQueryRaw<string>(
                """
                SELECT table_name || '.' || column_name AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'public' AND data_type = 'timestamp without time zone'
                """)
            .ToListAsync();

        // Hai chiều: cột naive trong Postgres phải có property tương ứng, và property khai báo naive
        // trong model phải THẬT SỰ là naive trong Postgres.
        Assert.Equal(
            dbNaive.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            modelNaive.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList());

        var missing = modelNaive.Where(kv => !ConvertsReadToUtc(kv.Value)).Select(kv => kv.Key).ToList();
        Assert.Empty(missing);
    }

    [ThrowawayDbFact]
    public async Task Postgres_roundtrip_keeps_wall_clock_and_reads_back_as_Utc_with_Z()
    {
        await using var db = await CreatePostgresContextAsync();

        var id = Guid.NewGuid();
        var assetId = await db.Database.SqlQueryRaw<Guid>("SELECT \"Id\" AS \"Value\" FROM assets LIMIT 1").FirstAsync();

        // Ghi ĐÚNG payload FE (MaintenanceListPage: dayjs(...).toISOString()) cho ngày 10/11/2026 giờ VN
        // = 2026-11-09T17:00:00Z — tức wall-clock naive 2026-11-09 17:00:00.
        await using (var write = await CreatePostgresContextAsync())
        {
            write.AssetMaintenances.Add(new aspire_react.Server.Domain.Entities.AssetMaintenance
            {
                Id = id,
                AssetId = assetId,
                Type = aspire_react.Server.Domain.Enums.AssetMaintenanceType.Maintenance,
                Title = "MT-TZ roundtrip probe",
                CompanyId = Guid.Empty,
                StartDate = new DateTime(2026, 11, 9, 17, 0, 0, DateTimeKind.Utc),
                CompletionDate = new DateTime(2026, 11, 10, 2, 30, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedById = await db.Database.SqlQueryRaw<Guid>("SELECT \"Id\" AS \"Value\" FROM users LIMIT 1").FirstAsync()
            });
            await write.SaveChangesAsync();
        }

        try
        {
            var rawStart = await db.Database
                .SqlQueryRaw<string>("SELECT \"StartDate\"::text AS \"Value\" FROM asset_maintenances WHERE \"Id\" = {0}", id)
                .FirstAsync();
            // Đọc từ cột naive = wall-clock (KHÔNG bị dịch múi giờ khi ghi).
            Assert.Equal("2026-11-09 17:00:00", rawStart);

            var row = await db.AssetMaintenances.AsNoTracking().FirstAsync(m => m.Id == id);

            // Đọc qua EF: Kind=Utc để JSON có hậu tố "Z" (FE format đúng theo giờ VN).
            Assert.Equal(DateTimeKind.Utc, row.StartDate.Kind);
            Assert.Equal(new DateTime(2026, 11, 9, 17, 0, 0), row.StartDate);
            Assert.Equal(DateTimeKind.Utc, row.CompletionDate!.Value.Kind);

            var json = JsonSerializer.Serialize(
                new { startDate = row.StartDate, completionDate = row.CompletionDate },
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Contains("\"startDate\":\"2026-11-09T17:00:00Z\"", json);
            Assert.Contains("\"completionDate\":\"2026-11-10T02:30:00Z\"", json);

            // PUT lặp lại (đổi field khác, giữ nguyên ngày) KHÔNG được đổi giá trị ngày trong DB.
            row.Notes = "MT-TZ roundtrip probe (touch)";
            db.AssetMaintenances.Update(row);
            await db.SaveChangesAsync();
            Assert.Equal(rawStart, await db.Database
                .SqlQueryRaw<string>("SELECT \"StartDate\"::text AS \"Value\" FROM asset_maintenances WHERE \"Id\" = {0}", id)
                .FirstAsync());
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM asset_maintenances WHERE \"Id\" = {0}", id);
        }
    }

    /// <summary>Mọi property DateTime/DateTime? map vào cột <see cref="NaiveStoreType"/> (khóa = "table.column").</summary>
    private static IEnumerable<KeyValuePair<string, IReadOnlyProperty>> NaiveProperties(AppDbContext db)
    {
        foreach (var entityType in db.Model.GetEntityTypes())
        {
            var table = entityType.GetTableName() ?? entityType.ClrType.Name;
            foreach (var property in entityType.GetProperties())
            {
                // Đọc ANNOTATION (không dùng GetColumnType(): provider InMemory ném InvalidCastException vì
                // nó không có RelationalTypeMapping — model vẫn giữ nguyên annotation do HasColumnType khai báo).
                var storeType = property.FindAnnotation(RelationalAnnotationNames.ColumnType)?.Value as string;
                if (!string.Equals(storeType, NaiveStoreType, StringComparison.OrdinalIgnoreCase))
                    continue;
                var column = property.FindAnnotation(RelationalAnnotationNames.ColumnName)?.Value as string ?? property.Name;
                yield return new KeyValuePair<string, IReadOnlyProperty>($"{table}.{column}", property);
            }
        }
    }

    /// <summary>Converter có hạ Kind xuống Unspecified khi GHI và nâng lên Utc khi ĐỌC hay không.</summary>
    private static bool ConvertsReadToUtc(IReadOnlyProperty property)
    {
        var converter = property.GetValueConverter();
        if (converter is null)
            return false;

        // Cột nullable dùng ValueConverter&lt;DateTime?, DateTime?&gt; ⇒ phải bỏ lớp Nullable trước khi so.
        var modelType = Nullable.GetUnderlyingType(converter.ModelClrType) ?? converter.ModelClrType;
        var providerType = Nullable.GetUnderlyingType(converter.ProviderClrType!) ?? converter.ProviderClrType;
        if (modelType != typeof(DateTime) || providerType != typeof(DateTime))
            return false;

        if (converter.ConvertToProvider(Probe) is not DateTime written || written.Kind != DateTimeKind.Unspecified)
            return false;
        return converter.ConvertFromProvider(Probe) is DateTime read && read.Kind == DateTimeKind.Utc;
    }

    private static async Task<AppDbContext> CreatePostgresContextAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnv)?.Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                $"Thiếu {ConnectionStringEnv} (connection string tới DB TẠM trong container Postgres RIÊNG).");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("aspire-react.Server"))
            .Options;

        var db = new AppDbContext(options, new TestHelpers.SuperUserScope());
        await db.Database.MigrateAsync();
        return db;
    }
}
