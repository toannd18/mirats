using System.Globalization;

namespace aspire_react.Server.Application.Reports.RepairReports;

/// <summary>
/// [REPAIR-REPORT] The ONLY place allowed to turn asset_maintenances.StartDate/CompletionDate
/// (naive timestamp, stored as UTC by NaiveUtcConverter) into a Vietnamese calendar date.
/// Keeping the conversion in one helper prevents ad-hoc `::date`, `.Date`, date_trunc or
/// MONTH()/YEAR() calls from drifting at the VN month/quarter/year boundary (MT-TZ-B).
/// </summary>
public static class VnReportDate
{
    public const string TimeZoneId = "Asia/Ho_Chi_Minh";

    private static readonly TimeZoneInfo VnTz = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    public static DateOnly TodayVn() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, VnTz));

    public static DateTime NowVn() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, VnTz);

    /// <summary>Interpret a naive DB timestamp as UTC and convert to the VN calendar date.</summary>
    public static DateOnly ToVnDate(DateTime naiveUtc)
    {
        var utc = DateTime.SpecifyKind(naiveUtc, DateTimeKind.Utc);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, VnTz));
    }

    /// <summary>VN 00:00 of a date as an absolute UTC instant (for timestamptz comparisons).</summary>
    public static DateTime StartOfDayUtc(DateOnly date) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), VnTz);

    /// <summary>SQL: naive UTC timestamp -> VN local timestamp (timestamp without time zone).</summary>
    public static string SqlLocalTimestamp(string alias, string column) =>
        $"{alias}.\"{column}\" AT TIME ZONE 'UTC' AT TIME ZONE '{TimeZoneId}'";

    /// <summary>SQL: naive UTC timestamp -> VN calendar date. ONLY use through this helper.</summary>
    public static string SqlDate(string alias, string column) =>
        $"({SqlLocalTimestamp(alias, column)})::date";

    /// <summary>SQL: timestamptz -> VN calendar date (asset_installations.StartedAt/EndedAt).</summary>
    public static string SqlTimestamptzDate(string alias, string column) =>
        $"({alias}.\"{column}\" AT TIME ZONE '{TimeZoneId}')::date";

    /// <summary>SQL: a VN date column -> VN 00:00 as timestamptz.</summary>
    public static string SqlStartOfDayVn(string alias, string dateColumn) =>
        $"{alias}.\"{dateColumn}\"::timestamp AT TIME ZONE '{TimeZoneId}'";

    /// <summary>SQL period key for grouping (month / quarter / year), derived from a VN date.</summary>
    public static string SqlPeriodKey(string alias, string dateColumn, string period) =>
        period switch
        {
            "quarter" => $"to_char({alias}.\"{dateColumn}\", 'YYYY-\"Q\"Q')",
            "year" => $"to_char({alias}.\"{dateColumn}\", 'YYYY')",
            _ => $"to_char({alias}.\"{dateColumn}\", 'YYYY-MM')",
        };

    public static string PeriodLabel(string periodKey, string period) =>
        period switch
        {
            "quarter" when periodKey.Length == 7 => $"Q{periodKey[^1]}/{periodKey[..4]}",
            "year" => periodKey,
            _ when periodKey.Length == 7 && DateTime.TryParseExact(
                periodKey + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) =>
                $"{d.Month:00}/{d.Year}",
            _ => periodKey,
        };
}
