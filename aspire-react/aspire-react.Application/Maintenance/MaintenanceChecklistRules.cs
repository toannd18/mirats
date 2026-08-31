using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;

namespace aspire_react.Server.Application.Maintenance;

/// <summary>
/// [A3] Business logic thuần của Maintenance Checklist — tách khỏi
/// MaintenanceCampaignsController (audit kiến trúc 2026-08-30): static helpers,
/// KHÔNG có dependency, giữ nguyên 100% công thức để controller và (tương lai)
/// handlers dùng chung một nguồn sự thật.
/// <para>
/// Frontend hiện mirror công thức EvaluateThreshold trong
/// MaintenanceCampaignDetailPage (evaluateIsPass) — việc đồng bộ nguồn sự thật
/// FE/BE là phạm vi riêng, không thuộc task này.
/// </para>
/// </summary>
public static class MaintenanceChecklistRules
{
    /// <summary>[MC-10] Trích số thập phân đầu tiên từ chuỗi đo ("55%" → 55, "12,5" → 12.5, "-3" → -3).</summary>
    public static bool TryParseMeasured(string? raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var m = System.Text.RegularExpressions.Regex.Match(raw.Trim(), @"-?\d+(?:[.,]\d+)?");
        if (!m.Success) return false;
        return decimal.TryParse(m.Value.Replace(',', '.'), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    /// <summary>[MC-10] Đánh giá Đạt/Không đạt theo toán tử ngưỡng.</summary>
    public static bool EvaluateThreshold(MaintenanceThresholdOperator op, decimal threshold, decimal measured)
        => op switch
        {
            MaintenanceThresholdOperator.LessThan => measured < threshold,
            MaintenanceThresholdOperator.LessOrEqual => measured <= threshold,
            MaintenanceThresholdOperator.GreaterThan => measured > threshold,
            MaintenanceThresholdOperator.GreaterOrEqual => measured >= threshold,
            _ => Math.Abs(measured - threshold) < 0.0001m // Equal
        };

    /// <summary>
    /// [MC-7c/MC-9] Số kết quả checklist CẦN THIẾT để hoàn thành campaign — "applicable pairs":
    /// mỗi item đếm (số snapshot áp dụng) × (số tiêu chuẩn, không có tiêu chuẩn thì 1).
    /// Item không khai báo vị trí (universal) áp dụng mọi snapshot; item khai báo vị trí chỉ
    /// áp dụng snapshot nằm trong danh sách vị trí đó.
    /// </summary>
    public static int CountExpectedResults(
        IReadOnlyCollection<MaintenanceChecklistItem> items,
        IReadOnlyCollection<MaintenanceCampaignDeviceSnapshot> snapshots)
        => items.Sum(it =>
        {
            var applicableSnapshots = it.Positions.Count == 0
                ? snapshots.Count
                : snapshots.Count(s => s.SystemPositionId.HasValue && it.Positions.Any(p => p.SystemPositionId == s.SystemPositionId.Value));
            var factor = it.StandardParams.Count == 0 ? 1 : it.StandardParams.Count;
            return applicableSnapshots * factor;
        });
}
