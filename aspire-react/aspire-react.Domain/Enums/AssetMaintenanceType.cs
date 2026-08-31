namespace aspire_react.Server.Domain.Enums;

/// <summary>
/// Asset maintenance categories (Snipe-IT AssetMaintenanceType equivalents).
/// IncidentReport = ghi nhận sự cố ngoài kế hoạch: StartDate = ngày phát hiện,
/// CompletionDate = ngày xử lý xong (hoặc null nếu đang chờ xử lý).
/// </summary>
public enum AssetMaintenanceType
{
    Maintenance = 1,        // Bảo trì định kỳ
    Repair = 2,             // Sửa chữa
    Upgrade = 3,            // Nâng cấp
    HardwareSupport = 4,    // Hỗ trợ phần cứng
    SoftwareSupport = 5,    // Hỗ trợ phần mềm
    PatTest = 6,
    Calibration = 7,        // Hiệu chuẩn
    IncidentReport = 8      // Báo cáo sự cố
}
