namespace aspire_react.Server.Domain.Enums;

/// <summary>
/// [MC-10] Toán tử so sánh ngưỡng cảnh báo của tiêu chuẩn kỹ thuật.
/// Cặp (ThresholdOperator, ThresholdValue) thay cho ô text tự do "VD: &lt;60%" —
/// cho phép máy TỰ ĐỘNG suy ra Đạt/Không đạt từ MeasuredValue.
/// Serializes as string (global JsonStringEnumConverter).
/// </summary>
public enum MaintenanceThresholdOperator
{
    LessThan = 0,
    LessOrEqual = 1,
    GreaterThan = 2,
    GreaterOrEqual = 3,
    Equal = 4
}