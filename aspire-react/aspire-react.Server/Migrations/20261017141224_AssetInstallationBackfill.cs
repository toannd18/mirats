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
    /// Sau INSERT, một khối PL/pgSQL kiểm tra bất biến LL-2 và <c>RAISE EXCEPTION</c> nếu còn lệch —
    /// migration FAIL thay vì để lại dữ liệu lệch im lặng (khối lệnh nằm trong transaction của migration
    /// nên toàn bộ backfill được rollback).
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

            // [LL-2b] KHÔNG để lệch im lặng: sau backfill, mọi asset đang gắn vị trí PHẢI có đúng 1 dòng
            // lý lịch đang mở (chưa superseded). Nếu còn lệch (VD asset có 2 dòng mở, hoặc bị bỏ qua vì
            // chồng khoảng) → RAISE EXCEPTION để migration FAIL và transaction của migration rollback.
            // Kèm vế ngược (không gắn vị trí mà vẫn còn dòng mở) vì đó cũng là phá bất biến của LL-2.
            migrationBuilder.Sql(
                """
                DO $ll2_backfill_check$
                DECLARE
                    mounted_bad integer;
                    mounted_tags text;
                    unmounted_bad integer;
                    unmounted_tags text;
                BEGIN
                    SELECT count(*), string_agg(a."AssetTag", ', ' ORDER BY a."AssetTag")
                      INTO mounted_bad, mounted_tags
                      FROM assets a
                     WHERE a."SystemPositionId" IS NOT NULL
                       AND (SELECT count(*) FROM asset_installations i
                             WHERE i."AssetId" = a."Id"
                               AND i."EndedAt" IS NULL
                               AND i."SupersededAt" IS NULL) <> 1;

                    IF mounted_bad > 0 THEN
                        RAISE EXCEPTION 'AssetInstallationBackfill FAILED: % asset(s) dang gan vi tri nhung khong co dung 1 dong ly lich mo: %', mounted_bad, mounted_tags;
                    END IF;

                    SELECT count(*), string_agg(a."AssetTag", ', ' ORDER BY a."AssetTag")
                      INTO unmounted_bad, unmounted_tags
                      FROM assets a
                     WHERE a."SystemPositionId" IS NULL
                       AND EXISTS (SELECT 1 FROM asset_installations i
                                    WHERE i."AssetId" = a."Id"
                                      AND i."EndedAt" IS NULL
                                      AND i."SupersededAt" IS NULL);

                    IF unmounted_bad > 0 THEN
                        RAISE EXCEPTION 'AssetInstallationBackfill FAILED: % asset(s) KHONG gan vi tri nhung con dong ly lich mo: %', unmounted_bad, unmounted_tags;
                    END IF;
                END
                $ll2_backfill_check$;
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
