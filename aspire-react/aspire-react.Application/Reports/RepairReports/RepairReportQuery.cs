using System.Data;
using System.Data.Common;
using System.Globalization;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace aspire_react.Server.Application.Reports.RepairReports;

public sealed record RepairReportGroupDto(
    string PeriodKey,
    string PeriodLabel,
    Guid? SystemId,
    string SystemName,
    string InstallationStatus,
    int Failures,
    int OpenCount,
    int CompletedCount,
    decimal? MttrDays,
    decimal TotalCost,
    int MissingCostCount,
    int WarrantyCount,
    decimal WarrantyRate);

public sealed record RepairReportItemDto(
    Guid Id,
    Guid AssetId,
    string AssetTag,
    string AssetName,
    string? ModelName,
    string? CategoryName,
    string Type,
    DateTime StartDate,
    DateTime? CompletionDate,
    bool IsOpen,
    decimal? Cost,
    bool IsWarranty,
    Guid? SystemId,
    string? SystemName,
    string InstallationStatus,
    string PeriodKey);

public sealed record RepairReportSummaryDto(
    int Failures,
    int OpenCount,
    int CompletedCount,
    decimal? MttrDays,
    decimal TotalCost,
    int MissingCostCount,
    int WarrantyCount,
    decimal WarrantyRate,
    int UnassignedCount,
    int InvalidInstallationCount);

public sealed record RepairReportResult(
    RepairReportSummaryDto Summary,
    IReadOnlyList<RepairReportGroupDto> Groups,
    IReadOnlyList<RepairReportItemDto> Items,
    string ScopeCompanyName,
    string FilterSummary);

public record RepairReportQuery(
    string Period,
    int? Year,
    int? Month,
    int? Quarter,
    Guid? SystemId,
    bool UnassignedOnly,
    string TypeGroup,
    Guid? CategoryId,
    Guid? ModelId) : IRequest<RepairReportResult>;

public sealed class RepairReportQueryHandler : IRequestHandler<RepairReportQuery, RepairReportResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public RepairReportQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<RepairReportResult> Handle(RepairReportQuery request, CancellationToken cancellationToken)
    {
        var period = NormalizePeriod(request.Period);
        var today = VnReportDate.TodayVn();
        var year = request.Year ?? today.Year;
        var (fromDate, toDate) = ResolveRange(period, year, request.Month, request.Quarter);
        var types = ResolveTypes(request.TypeGroup);
        var companyId = await _companyScope.GetCurrentUserCompanyIdAsync();

        var connection = _context.Database.GetDbConnection();
        var wasOpen = connection.State == ConnectionState.Open;
        if (!wasOpen) await connection.OpenAsync(cancellationToken);
        try
        {
            var summarySql = RepairReportSql.Summary(period);
            var groupsSql = RepairReportSql.Groups(period);
            var itemsSql = RepairReportSql.Items(period);

            var summary = await ReadSummaryAsync(connection, summarySql, request, fromDate, toDate, types, companyId, cancellationToken);
            var groups = await ReadGroupsAsync(connection, groupsSql, request, period, fromDate, toDate, types, companyId, cancellationToken);
            var items = await ReadItemsAsync(connection, itemsSql, request, fromDate, toDate, types, companyId, cancellationToken);
            var scopeCompanyName = companyId is null
                ? "Tất cả đơn vị"
                : await _context.Companies.AsNoTracking()
                    .Where(c => c.Id == companyId.Value)
                    .Select(c => c.Name)
                    .FirstOrDefaultAsync(cancellationToken) ?? "Không xác định";
            var filterSummary = await BuildFilterSummaryAsync(request, period, year, fromDate, toDate, cancellationToken);
            return new RepairReportResult(summary, groups, items, scopeCompanyName, filterSummary);
        }
        finally
        {
            if (!wasOpen) await connection.CloseAsync();
        }
    }

    private async Task<string> BuildFilterSummaryAsync(
        RepairReportQuery request, string period, int year, DateOnly fromDate, DateOnly toDate,
        CancellationToken cancellationToken)
    {
        var periodText = period switch
        {
            "quarter" when request.Quarter is > 0 => $"Quý {request.Quarter}/{year}",
            "quarter" => $"Cả năm {year} (nhóm theo quý)",
            "month" when request.Month is > 0 => $"Tháng {request.Month}/{year}",
            "month" => $"Cả năm {year} (nhóm theo tháng)",
            _ => $"Năm {year}",
        };

        var systemText = request.UnassignedOnly
            ? "Không thuộc hệ thống"
            : request.SystemId is null
                ? "Tất cả hệ thống"
                : await _context.SystemInfos.AsNoTracking()
                    .Where(s => s.Id == request.SystemId.Value)
                    .Select(s => s.Name)
                    .FirstOrDefaultAsync(cancellationToken) ?? request.SystemId.Value.ToString();

        var categoryText = request.CategoryId is null
            ? "Tất cả"
            : await _context.Categories.AsNoTracking()
                .Where(c => c.Id == request.CategoryId.Value)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken) ?? request.CategoryId.Value.ToString();

        var modelText = request.ModelId is null
            ? "Tất cả"
            : await _context.Models.AsNoTracking()
                .Where(m => m.Id == request.ModelId.Value)
                .Select(m => m.Name)
                .FirstOrDefaultAsync(cancellationToken) ?? request.ModelId.Value.ToString();

        var typeText = request.TypeGroup?.ToLowerInvariant() switch
        {
            "all" => "Tất cả loại bảo trì",
            "repair_incident_hardware" => "Sửa chữa + Sự cố + Hỗ trợ phần cứng",
            _ => "Sửa chữa + Sự cố",
        };

        return $"{periodText} · Hệ thống: {systemText} · Nhóm loại: {typeText} · Danh mục: {categoryText} · Model: {modelText} · Từ {fromDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} đến {toDate.AddDays(-1).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";
    }

    private static (DateOnly From, DateOnly To) ResolveRange(
        string period, int year, int? month, int? quarter)
    {
        return period switch
        {
            "quarter" => quarter is > 0
                ? QuarterRange(year, quarter.Value)
                : (new DateOnly(year, 1, 1), new DateOnly(year + 1, 1, 1)),
            "year" => (new DateOnly(year, 1, 1), new DateOnly(year + 1, 1, 1)),
            _ => month is > 0
                ? MonthRange(year, month.Value)
                : (new DateOnly(year, 1, 1), new DateOnly(year + 1, 1, 1)),
        };
    }

    private static (DateOnly, DateOnly) MonthRange(int year, int month)
    {
        var m = Math.Clamp(month, 1, 12);
        var from = new DateOnly(year, m, 1);
        return (from, from.AddMonths(1));
    }

    private static (DateOnly, DateOnly) QuarterRange(int year, int quarter)
    {
        var q = Math.Clamp(quarter, 1, 4);
        var from = new DateOnly(year, (q - 1) * 3 + 1, 1);
        return (from, from.AddMonths(3));
    }

    private static string NormalizePeriod(string? value) =>
        value?.Trim().ToLowerInvariant() is "quarter" or "year" ? value.Trim().ToLowerInvariant() : "month";
    private static int[] ResolveTypes(string? group) => group?.Trim().ToLowerInvariant() switch
    {
        "all" => [1, 2, 3, 4, 5, 6, 7, 8],
        "repair_incident_hardware" => [2, 4, 8],
        _ => [2, 8],
    };

    private static DbCommand CreateCommand(DbConnection connection, string sql, RepairReportQuery request,
        DateOnly fromDate, DateOnly toDate, int[] types, Guid? companyId)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter("@fromDate", NpgsqlDbType.Date) { Value = fromDate });
        cmd.Parameters.Add(new NpgsqlParameter("@toDate", NpgsqlDbType.Date) { Value = toDate });
        cmd.Parameters.Add(new NpgsqlParameter("@types", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = types });
        cmd.Parameters.Add(new NpgsqlParameter("@companyId", NpgsqlDbType.Uuid) { Value = companyId is null ? DBNull.Value : companyId.Value });
        cmd.Parameters.Add(new NpgsqlParameter("@systemId", NpgsqlDbType.Uuid) { Value = request.SystemId is null ? DBNull.Value : request.SystemId.Value });
        cmd.Parameters.Add(new NpgsqlParameter("@unassignedOnly", NpgsqlDbType.Boolean) { Value = request.UnassignedOnly });
        cmd.Parameters.Add(new NpgsqlParameter("@categoryId", NpgsqlDbType.Uuid) { Value = request.CategoryId is null ? DBNull.Value : request.CategoryId.Value });
        cmd.Parameters.Add(new NpgsqlParameter("@modelId", NpgsqlDbType.Uuid) { Value = request.ModelId is null ? DBNull.Value : request.ModelId.Value });
        return cmd;
    }

    private static async Task<RepairReportSummaryDto> ReadSummaryAsync(DbConnection connection, string sql,
        RepairReportQuery request, DateOnly fromDate, DateOnly toDate, int[] types, Guid? companyId,
        CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(connection, sql, request, fromDate, toDate, types, companyId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return new RepairReportSummaryDto(0, 0, 0, null, 0, 0, 0, 0, 0, 0);

        var failures = Convert.ToInt32(reader.GetValue(0));
        var completed = Convert.ToInt32(reader.GetValue(2));
        var warranty = Convert.ToInt32(reader.GetValue(6));
        return new RepairReportSummaryDto(
            Failures: failures,
            OpenCount: Convert.ToInt32(reader.GetValue(1)),
            CompletedCount: completed,
            MttrDays: RoundMttrDays(ReadNullableDecimal(reader, 3)),
            TotalCost: ReadDecimal(reader, 4),
            MissingCostCount: Convert.ToInt32(reader.GetValue(5)),
            WarrantyCount: warranty,
            WarrantyRate: failures == 0 ? 0 : Math.Round((decimal)warranty / failures, 4),
            UnassignedCount: Convert.ToInt32(reader.GetValue(7)),
            InvalidInstallationCount: Convert.ToInt32(reader.GetValue(8)));
    }

    private static async Task<IReadOnlyList<RepairReportGroupDto>> ReadGroupsAsync(DbConnection connection, string sql,
        RepairReportQuery request, string period, DateOnly fromDate, DateOnly toDate, int[] types, Guid? companyId,
        CancellationToken cancellationToken)
    {
        var result = new List<RepairReportGroupDto>();
        await using var cmd = CreateCommand(connection, sql, request, fromDate, toDate, types, companyId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = reader.GetString(0);
            var failures = Convert.ToInt32(reader.GetValue(4));
            var warranty = Convert.ToInt32(reader.GetValue(9));
            result.Add(new RepairReportGroupDto(
                PeriodKey: key,
                PeriodLabel: VnReportDate.PeriodLabel(key, period),
                SystemId: reader.IsDBNull(1) ? null : reader.GetGuid(1),
                SystemName: !reader.IsDBNull(2)
                    ? reader.GetString(2)
                    : reader.GetString(3) == "Invalid" ? "Dữ liệu lý lịch không hợp lệ" : "Không thuộc hệ thống",
                InstallationStatus: reader.GetString(3),
                Failures: failures,
                OpenCount: Convert.ToInt32(reader.GetValue(5)),
                CompletedCount: Convert.ToInt32(reader.GetValue(6)),
                MttrDays: RoundMttrDays(ReadNullableDecimal(reader, 7)),
                TotalCost: ReadDecimal(reader, 8),
                MissingCostCount: Convert.ToInt32(reader.GetValue(10)),
                WarrantyCount: warranty,
                WarrantyRate: failures == 0 ? 0 : Math.Round((decimal)warranty / failures, 4)));
        }
        return result;
    }

    private static async Task<IReadOnlyList<RepairReportItemDto>> ReadItemsAsync(DbConnection connection, string sql,
        RepairReportQuery request, DateOnly fromDate, DateOnly toDate, int[] types, Guid? companyId,
        CancellationToken cancellationToken)
    {
        var result = new List<RepairReportItemDto>();
        await using var cmd = CreateCommand(connection, sql, request, fromDate, toDate, types, companyId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var type = (AssetMaintenanceType)Convert.ToInt32(reader.GetValue(6));
            result.Add(new RepairReportItemDto(
                Id: reader.GetGuid(0),
                AssetId: reader.GetGuid(1),
                AssetTag: reader.GetString(2),
                AssetName: reader.GetString(3),
                ModelName: reader.IsDBNull(4) ? null : reader.GetString(4),
                CategoryName: reader.IsDBNull(5) ? null : reader.GetString(5),
                Type: type.ToString(),
                StartDate: ReadDate(reader, 7),
                CompletionDate: ReadNullableDate(reader, 8),
                IsOpen: reader.IsDBNull(8),
                Cost: ReadNullableDecimal(reader, 9),
                IsWarranty: reader.GetBoolean(10),
                SystemId: reader.IsDBNull(11) ? null : reader.GetGuid(11),
                SystemName: reader.IsDBNull(12) ? null : reader.GetString(12),
                InstallationStatus: reader.GetString(13),
                PeriodKey: reader.GetString(14)));
        }
        return result;
    }

    private static decimal ReadDecimal(DbDataReader reader, int index) =>
        reader.IsDBNull(index) ? 0 : Convert.ToDecimal(reader.GetValue(index));

    private static decimal? ReadNullableDecimal(DbDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : Convert.ToDecimal(reader.GetValue(index));

    /// <summary>
    /// [REPAIR-REPORT] MTTR is presented with exactly one decimal everywhere (screen, Excel,
    /// print). Round once at the source so every consumer receives the same value; do not
    /// rely on per-consumer display formatting (different providers round differently).
    /// </summary>
    private static decimal? RoundMttrDays(decimal? value) =>
        value is null ? null : Math.Round(value.Value, 1, MidpointRounding.AwayFromZero);

    private static DateTime ReadDate(DbDataReader reader, int index) =>
        ToDateTime(reader.GetValue(index));

    private static DateTime? ReadNullableDate(DbDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : ToDateTime(reader.GetValue(index));

    private static DateTime ToDateTime(object value) => value switch
    {
        DateOnly d => d.ToDateTime(TimeOnly.MinValue),
        DateTime d => DateTime.SpecifyKind(d.Date, DateTimeKind.Unspecified),
        _ => Convert.ToDateTime(value),
    };
}

/// <summary>
/// SQL for the repair report. All StartDate/CompletionDate -> VN date conversion goes through
/// <see cref="VnReportDate"/>; the CTE is shared by summary/group/detail queries so the three
/// queries never drift (and no N+1 per maintenance row).
/// </summary>
internal static class RepairReportSql
{
    private const string EmptyCompany = "00000000-0000-0000-0000-000000000000";

    private static string MapCte(string period)
    {
        var startDate = VnReportDate.SqlDate("m", "StartDate");
        var completionDate = VnReportDate.SqlDate("m", "CompletionDate");
        var startOfDayCounts = VnReportDate.SqlStartOfDayVn("b", "StartDateVn");
        var startOfDayMapped = VnReportDate.SqlStartOfDayVn("c", "StartDateVn");
        var sameDayStartCounts = VnReportDate.SqlTimestamptzDate("i", "StartedAt");
        var sameDayStartMapped = VnReportDate.SqlTimestamptzDate("i", "StartedAt");
        var periodKey = VnReportDate.SqlPeriodKey("c", "StartDateVn", period);

        return $$"""
            WITH base AS (
                SELECT m."Id", m."AssetId", m."Type", m."Cost", m."IsWarranty",
                       a."AssetTag", a."Name" AS "AssetName",
                       mo."Name" AS "ModelName", ca."Name" AS "CategoryName",
                       {{startDate}} AS "StartDateVn",
                       {{completionDate}} AS "CompletionDateVn"
                FROM asset_maintenances m
                JOIN assets a ON a."Id" = m."AssetId"
                LEFT JOIN models mo ON mo."Id" = a."ModelId"
                LEFT JOIN categories ca ON ca."Id" = mo."CategoryId"
                WHERE m."DeletedAt" IS NULL
                  AND m."Type" = ANY(@types)
                  AND {{startDate}} >= @fromDate
                  AND {{startDate}} < @toDate
                  AND (@companyId IS NULL OR m."CompanyId" = @companyId OR m."CompanyId" = '{{EmptyCompany}}'::uuid)
                  AND (@categoryId IS NULL OR ca."Id" = @categoryId)
                  AND (@modelId IS NULL OR mo."Id" = @modelId)
            ),
            counts AS (
                SELECT b.*,
                       (SELECT count(*) FROM asset_installations i
                        WHERE i."AssetId" = b."AssetId" AND i."SupersededAt" IS NULL
                          AND i."StartedAt" <= {{startOfDayCounts}}
                          AND (i."EndedAt" IS NULL OR i."EndedAt" > {{startOfDayCounts}})) AS "ActiveCount",
                       (SELECT count(*) FROM asset_installations i
                        WHERE i."AssetId" = b."AssetId" AND i."SupersededAt" IS NULL
                          AND {{sameDayStartCounts}} = b."StartDateVn") AS "SameDayCount"
                FROM base b
            ),
            mapped AS (
                SELECT c.*,
                       CASE
                         WHEN c."ActiveCount" > 1 THEN 'Invalid'
                         WHEN c."ActiveCount" = 1 THEN 'AtStart'
                         WHEN c."SameDayCount" > 1 THEN 'Invalid'
                         WHEN c."SameDayCount" = 1 THEN 'SameDay'
                         ELSE 'Unassigned'
                       END AS "InstallationStatus",
                       CASE WHEN c."ActiveCount" = 1 THEN ast."SystemInfoId"
                            WHEN c."ActiveCount" = 0 AND c."SameDayCount" = 1 THEN sd."SystemInfoId"
                       END AS "SystemId",
                       CASE WHEN c."ActiveCount" = 1 THEN ast."SystemName"
                            WHEN c."ActiveCount" = 0 AND c."SameDayCount" = 1 THEN sd."SystemName"
                       END AS "SystemName",
                       {{periodKey}} AS "PeriodKey"
                FROM counts c
                LEFT JOIN LATERAL (
                    SELECT i."SystemInfoId", COALESCE(i."SystemInfoName", si."Name") AS "SystemName"
                    FROM asset_installations i
                    LEFT JOIN system_infos si ON si."Id" = i."SystemInfoId"
                    WHERE i."AssetId" = c."AssetId" AND i."SupersededAt" IS NULL
                      AND i."StartedAt" <= {{startOfDayMapped}}
                      AND (i."EndedAt" IS NULL OR i."EndedAt" > {{startOfDayMapped}})
                    ORDER BY i."StartedAt"
                    LIMIT 1
                ) ast ON true
                LEFT JOIN LATERAL (
                    SELECT i."SystemInfoId", COALESCE(i."SystemInfoName", si."Name") AS "SystemName"
                    FROM asset_installations i
                    LEFT JOIN system_infos si ON si."Id" = i."SystemInfoId"
                    WHERE i."AssetId" = c."AssetId" AND i."SupersededAt" IS NULL
                      AND {{sameDayStartMapped}} = c."StartDateVn"
                    ORDER BY i."StartedAt"
                    LIMIT 1
                ) sd ON true
            ),
            filtered AS (
                SELECT * FROM mapped
                WHERE (@systemId IS NULL OR "SystemId" = @systemId)
                  AND (@unassignedOnly = FALSE OR "InstallationStatus" = 'Unassigned')
            )
            """;
    }

    public static string Summary(string period) => MapCte(period) + """

            SELECT count(*) AS "Failures",
                   count(*) FILTER (WHERE "CompletionDateVn" IS NULL) AS "OpenCount",
                   count(*) FILTER (WHERE "CompletionDateVn" IS NOT NULL) AS "CompletedCount",
                   AVG(("CompletionDateVn" - "StartDateVn")) FILTER (
                       WHERE "CompletionDateVn" IS NOT NULL AND "CompletionDateVn" >= "StartDateVn") AS "MttrDays",
                   COALESCE(SUM("Cost"), 0) AS "TotalCost",
                   count(*) FILTER (WHERE "Cost" IS NULL) AS "MissingCostCount",
                   count(*) FILTER (WHERE "IsWarranty") AS "WarrantyCount",
                   count(*) FILTER (WHERE "InstallationStatus" = 'Unassigned') AS "UnassignedCount",
                   count(*) FILTER (WHERE "InstallationStatus" = 'Invalid') AS "InvalidInstallationCount"
            FROM filtered;
            """;

    public static string Groups(string period) => MapCte(period) + """

            SELECT "PeriodKey",
                   "SystemId",
                   "SystemName",
                   "InstallationStatus",
                   count(*) AS "Failures",
                   count(*) FILTER (WHERE "CompletionDateVn" IS NULL) AS "OpenCount",
                   count(*) FILTER (WHERE "CompletionDateVn" IS NOT NULL) AS "CompletedCount",
                   AVG(("CompletionDateVn" - "StartDateVn")) FILTER (
                       WHERE "CompletionDateVn" IS NOT NULL AND "CompletionDateVn" >= "StartDateVn") AS "MttrDays",
                   COALESCE(SUM("Cost"), 0) AS "TotalCost",
                   count(*) FILTER (WHERE "IsWarranty") AS "WarrantyCount",
                   count(*) FILTER (WHERE "Cost" IS NULL) AS "MissingCostCount"
            FROM filtered
            GROUP BY "PeriodKey", "SystemId", "SystemName", "InstallationStatus"
            ORDER BY "PeriodKey" DESC, "SystemName" NULLS LAST;
            """;

    public static string Items(string period) => MapCte(period) + """

            SELECT "Id", "AssetId", "AssetTag", "AssetName", "ModelName", "CategoryName",
                   "Type", "StartDateVn", "CompletionDateVn", "Cost", "IsWarranty",
                   "SystemId", "SystemName", "InstallationStatus", "PeriodKey"
            FROM filtered
            ORDER BY "StartDateVn" DESC, "AssetTag";
            """;
}
