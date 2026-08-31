namespace aspire_react.Server.Infrastructure.Authorization;

/// <summary>
/// Định nghĩa một permission key (ví dụ "assets.view").
/// </summary>
public sealed record PermissionDefinition(string Code, string Resource, string Action, string Description);

/// <summary>
/// Nguồn duy nhất (single source of truth) cho mọi permission key được dùng bởi
/// <c>[Authorize(Policy = "...")]</c>. Vừa điều khiển việc đăng ký policy trong
/// <c>Program.cs AddAuthorization</c>, vừa cung cấp dữ liệu cho endpoint
/// <c>GET /api/v1/permissions</c> (catalog cho frontend). Thêm/sửa permission chỉ cần sửa file này.
/// </summary>
public static class PermissionCatalog
{
    public static readonly IReadOnlyList<PermissionDefinition> All = new List<PermissionDefinition>
    {
        // === Tài sản (Assets) ===
        new("assets.view", "assets", "view", "Xem tài sản"),
        new("assets.create", "assets", "create", "Tạo tài sản"),
        new("assets.edit", "assets", "edit", "Cập nhật tài sản"),
        new("assets.delete", "assets", "delete", "Xóa tài sản"),
        new("assets.checkout", "assets", "checkout", "Cấp phát tài sản"),
        new("assets.checkin", "assets", "checkin", "Thu hồi tài sản"),
        new("assets.audit", "assets", "audit", "Kiểm kê tài sản"),

        // === Vật tư tiêu hao (Consumables) ===
        new("consumables.view", "consumables", "view", "Xem vật tư tiêu hao"),
        new("consumables.create", "consumables", "create", "Tạo vật tư tiêu hao"),
        new("consumables.edit", "consumables", "edit", "Cập nhật vật tư tiêu hao"),
        new("consumables.delete", "consumables", "delete", "Xóa vật tư tiêu hao"),
        new("consumables.checkout", "consumables", "checkout", "Cấp phát vật tư tiêu hao"),

        // === Linh kiện (Components) ===
        new("components.view", "components", "view", "Xem linh kiện"),
        new("components.create", "components", "create", "Tạo linh kiện"),
        new("components.edit", "components", "edit", "Cập nhật linh kiện"),
        new("components.delete", "components", "delete", "Xóa linh kiện"),
        new("components.checkout", "components", "checkout", "Cấp phát/điều chuyển linh kiện"),

        // === Phụ kiện (Accessories) ===
        new("accessories.view", "accessories", "view", "Xem phụ kiện"),
        new("accessories.create", "accessories", "create", "Tạo phụ kiện"),
        new("accessories.edit", "accessories", "edit", "Cập nhật phụ kiện"),
        new("accessories.delete", "accessories", "delete", "Xóa phụ kiện"),
        new("accessories.checkout", "accessories", "checkout", "Cấp phát phụ kiện"),

        // === Bản quyền (Licenses) ===
        new("licenses.view", "licenses", "view", "Xem bản quyền"),
        new("licenses.create", "licenses", "create", "Tạo bản quyền"),
        new("licenses.edit", "licenses", "edit", "Cập nhật bản quyền"),
        new("licenses.delete", "licenses", "delete", "Xóa bản quyền"),
        new("licenses.checkout", "licenses", "checkout", "Cấp phát/thu hồi seat bản quyền"),

        // === Người dùng (Users) ===
        new("users.view", "users", "view", "Xem người dùng"),
        new("users.create", "users", "create", "Tạo người dùng"),
        new("users.edit", "users", "edit", "Cập nhật người dùng"),
        new("users.delete", "users", "delete", "Vô hiệu hóa người dùng"),

        // === Công ty (Companies) ===
        new("companies.view", "companies", "view", "Xem công ty"),
        new("companies.create", "companies", "create", "Tạo công ty"),
        new("companies.edit", "companies", "edit", "Cập nhật công ty"),
        new("companies.delete", "companies", "delete", "Xóa công ty"),

        // === Model tài sản (Asset Models) ===
        new("models.view", "models", "view", "Xem model tài sản"),
        new("models.create", "models", "create", "Tạo model tài sản"),
        new("models.edit", "models", "edit", "Cập nhật model tài sản"),
        new("models.delete", "models", "delete", "Xóa model tài sản"),

        // === Danh mục (Categories) ===
        new("categories.view", "categories", "view", "Xem danh mục"),
        new("categories.create", "categories", "create", "Tạo danh mục"),
        new("categories.edit", "categories", "edit", "Cập nhật danh mục"),
        new("categories.delete", "categories", "delete", "Xóa danh mục"),

        // === Nhà sản xuất (Manufacturers) ===
        new("manufacturers.view", "manufacturers", "view", "Xem nhà sản xuất"),
        new("manufacturers.create", "manufacturers", "create", "Tạo nhà sản xuất"),
        new("manufacturers.edit", "manufacturers", "edit", "Cập nhật nhà sản xuất"),
        new("manufacturers.delete", "manufacturers", "delete", "Xóa nhà sản xuất"),

        // === Nhà cung cấp (Suppliers) ===
        new("suppliers.view", "suppliers", "view", "Xem nhà cung cấp"),
        new("suppliers.create", "suppliers", "create", "Tạo nhà cung cấp"),
        new("suppliers.edit", "suppliers", "edit", "Cập nhật nhà cung cấp"),
        new("suppliers.delete", "suppliers", "delete", "Xóa nhà cung cấp"),

        // === Phòng ban (Departments) ===
        new("departments.view", "departments", "view", "Xem phòng ban"),
        new("departments.create", "departments", "create", "Tạo phòng ban"),
        new("departments.edit", "departments", "edit", "Cập nhật phòng ban"),
        new("departments.delete", "departments", "delete", "Xóa phòng ban"),

        // === Địa điểm (Locations) ===
        new("locations.view", "locations", "view", "Xem địa điểm"),
        new("locations.create", "locations", "create", "Tạo địa điểm"),
        new("locations.edit", "locations", "edit", "Cập nhật địa điểm"),
        new("locations.delete", "locations", "delete", "Xóa địa điểm"),

        // === Trạng thái (Status Labels) ===
        new("statuslabels.view", "statuslabels", "view", "Xem trạng thái"),
        new("statuslabels.create", "statuslabels", "create", "Tạo trạng thái"),
        new("statuslabels.edit", "statuslabels", "edit", "Cập nhật trạng thái"),
        new("statuslabels.delete", "statuslabels", "delete", "Xóa trạng thái"),

        // === Khấu hao (Depreciations) ===
        new("depreciations.view", "depreciations", "view", "Xem cấu hình khấu hao"),
        new("depreciations.create", "depreciations", "create", "Tạo cấu hình khấu hao"),
        new("depreciations.edit", "depreciations", "edit", "Cập nhật cấu hình khấu hao"),
        new("depreciations.delete", "depreciations", "delete", "Xóa cấu hình khấu hao"),

        // === Báo cáo (Reports) ===
        new("reports.view", "reports", "view", "Xem báo cáo"),

        // === Import / Export ===
        new("import", "system", "import", "Import dữ liệu"),
        new("export", "system", "export", "Export dữ liệu"),

        // === Custom Fields ===
        new("customfields.view", "customfields", "view", "Xem trường tùy chỉnh"),
        new("customfields.create", "customfields", "create", "Tạo trường tùy chỉnh"),
        new("customfields.edit", "customfields", "edit", "Cập nhật trường tùy chỉnh"),
        new("customfields.delete", "customfields", "delete", "Xóa trường tùy chỉnh"),

        // === Hệ thống (System) ===
        new("systems.view", "systems", "view", "Xem hệ thống & vị trí"),
        new("systems.create", "systems", "create", "Tạo hệ thống / vị trí"),
        new("systems.edit", "systems", "edit", "Cập nhật hệ thống / vị trí"),
        new("systems.delete", "systems", "delete", "Xóa hệ thống / vị trí"),

        // === Bảo dưỡng định kỳ theo checklist (MC — Maintenance Checklist) ===
        new("maintenance.templates", "maintenance", "templates", "Quản lý Template & version checklist bảo dưỡng (xem/tạo/sửa/publish)"),
        new("maintenance.campaigns", "maintenance", "campaigns", "Quản lý đợt bảo dưỡng (tạo/hoàn thành/ghi kết quả checklist)"),
        new("maintenance.view", "maintenance", "view", "Xem lịch sử đợt bảo dưỡng & kết quả checklist"),
        new("admin", "system", "admin", "Quyền quản trị hệ thống (wildcard cho mọi quyền khác)"),
        new("superuser", "system", "superuser", "Toàn quyền tuyệt đối (bypass mọi kiểm tra)"),
        new("system.config", "system", "config", "Cấu hình hệ thống (VD format tự sinh Mã tài sản)")
    };
}

