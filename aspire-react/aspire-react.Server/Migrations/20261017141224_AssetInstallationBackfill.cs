using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace aspirereact.Server.Migrations
{
    /// <summary>
    /// [LL-2] Backfill lý lịch lắp đặt cho các thiết bị ĐANG gắn vị trí (<c>assets.SystemPositionId</c>
    /// IS NOT NULL) nhưng chưa có lý lịch — trạng thái hợp lệ trước LL-2 vì trước LL-2 không có đường nào
    /// ghi bảng <c>asset_installations</c>.
    /// <para>
    /// Không có thay đổi schema (Up/Down do EF sinh ra rỗng); migration này CHỈ chạy SQL dữ liệu.
    /// </para>
    /// <inheritdoc />
    public partial class AssetInstallationBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            //  - StartedAt  = COALESCE(a."LastCheckout", a."CreatedAt") — ƯỚC TÍNH (ghi rõ trong Note);
            //    thời điểm gắn thật không còn được lưu ở nơi nào khác.
            //  - EndedAt = NULL (khoảng đang mở) · SupersededAt/CorrectsEntryId = NULL.
            //  - Source = 3 (AssetInstallationSource.Backfill) · LoggedById/LoggedByName = NULL (không tác nhân).
            //  - Idempotent: NOT EXISTS theo KHOẢNG CHỒNG LẤN. Điều kiện "chưa có dòng mở" là tập con của
            //    điều kiện này; dùng dạng chồng lấn để an toàn cả khi DB đã có sẵn dòng ĐÓNG của cùng asset
            //    (chèn thêm sẽ vi phạm EX_asset_installations_no_overlap và làm hỏng migration).
            migrationBuilder.Sql(
                """
                INSERT INTO asset_installations (
                    "Id", "AssetId", "SystemPositionId", "SystemPositionName", "SystemInfoId", "SystemInfoName",
                    "StartedAt", "EndedAt", "SupersededAt", "CorrectsEntryId", "Note", "LoggedById", "LoggedByName",
                    "Source", "CreatedAt", "UpdatedAt")
                SELECT
                    gen_random_uuid(),
                    a."Id",
                    a."SystemPositionId",
                    sp."Name",
                    sp."SystemInfoId",
                    si."Name",
                    COALESCE(a."LastCheckout", a."CreatedAt"),
                    NULL, NULL, NULL,
                    'Backfill LL-2: thiết bị đang gắn vị trí nhưng chưa có lý lịch lắp đặt. Thời điểm bắt đầu gắn là ƯỚC TÍNH (COALESCE(LastCheckout, CreatedAt)).',
                    NULL, NULL,
                    3,
                    now(), now()
                FROM assets a
                JOIN system_positions sp ON sp."Id" = a."SystemPositionId"
                LEFT JOIN system_infos si ON si."Id" = sp."SystemInfoId"
                WHERE a."SystemPositionId" IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1
                      FROM asset_installations ai
                      WHERE ai."AssetId" = a."Id"
                        AND ai."SupersededAt" IS NULL
                        AND tstzrange(ai."StartedAt", ai."EndedAt", '[)')
                            && tstzrange(COALESCE(a."LastCheckout", a."CreatedAt"), NULL, '[)')
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Gỡ ĐÚNG (và chỉ) các dòng do backfill tạo — nhận diện bằng Source = 3 (Backfill).
            // Dòng do API ghi có Source = 1 nên không bị ảnh hưởng.
            migrationBuilder.Sql("DELETE FROM asset_installations WHERE \"Source\" = 3;");
        }
    }
}
