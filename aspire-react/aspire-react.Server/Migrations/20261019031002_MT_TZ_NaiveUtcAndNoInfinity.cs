using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace aspirereact.Server.Migrations
{
    /// <inheritdoc />
    public partial class MT_TZ_NaiveUtcAndNoInfinity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ==================== [MT-TZ] Bước 1: dọn giá trị vô cực TRƯỚC khi thêm CHECK ====================
            //
            // 16 cột `timestamp without time zone` là wall-clock UTC của .NET. Postgres vẫn nhận hai literal
            // '-infinity'/'infinity' nhưng .NET chỉ có DateTime.MinValue/MaxValue ⇒ Npgsql đọc '-infinity'
            // thành MinValue (HTTP 200, JSON "0001-01-01T00:00:00") và lần UPDATE sau sẽ ghi lại thành
            // 0001-01-01 THẬT (mất dấu vô cực). Quy ước dọn: cột NULLABLE → NULL; cột NOT NULL → CreatedAt
            // của chính dòng đó (nếu chính CreatedAt cũng vô cực thì dùng CURRENT_TIMESTAMP — nhánh này
            // chỉ để CHECK bên dưới không thể fail, thực tế 0 dòng).
            //
            // Cột NULLABLE (8): action_logs.DeletedAt · asset_maintenances.{ClosedAt, CompletionDate,
            // DeletedAt, InspectedAt} · component_units.DeletedAt · licenses.{DeletedAt, TerminationDate}.
            migrationBuilder.Sql("""
                UPDATE "action_logs" SET "DeletedAt" = NULL
                    WHERE "DeletedAt" IN ('-infinity'::timestamp, 'infinity'::timestamp);
                """);
            migrationBuilder.Sql("""
                UPDATE "asset_maintenances" SET "ClosedAt" = NULL
                    WHERE "ClosedAt" IN ('-infinity'::timestamp, 'infinity'::timestamp);
                """);
            migrationBuilder.Sql("""
                UPDATE "asset_maintenances" SET "CompletionDate" = NULL
                    WHERE "CompletionDate" IN ('-infinity'::timestamp, 'infinity'::timestamp);
                """);
            migrationBuilder.Sql("""
                UPDATE "asset_maintenances" SET "DeletedAt" = NULL
                    WHERE "DeletedAt" IN ('-infinity'::timestamp, 'infinity'::timestamp);
                """);
            migrationBuilder.Sql("""
                UPDATE "asset_maintenances" SET "InspectedAt" = NULL
                    WHERE "InspectedAt" IN ('-infinity'::timestamp, 'infinity'::timestamp);
                """);
            migrationBuilder.Sql("""
                UPDATE "component_units" SET "DeletedAt" = NULL
                    WHERE "DeletedAt" IN ('-infinity'::timestamp, 'infinity'::timestamp);
                """);
            migrationBuilder.Sql("""
                UPDATE "licenses" SET "DeletedAt" = NULL
                    WHERE "DeletedAt" IN ('-infinity'::timestamp, 'infinity'::timestamp);
                """);
            migrationBuilder.Sql("""
                UPDATE "licenses" SET "TerminationDate" = NULL
                    WHERE "TerminationDate" IN ('-infinity'::timestamp, 'infinity'::timestamp);
                """);

            // Cột NOT NULL (8): asset_maintenance_assignees.AssignedAt · asset_maintenances.{CreatedAt,
            // StartDate, UpdatedAt} · component_units.{CreatedAt, UpdatedAt} · license_seats.{CreatedAt, UpdatedAt}.
            // Riêng asset_maintenance_assignees KHÔNG có cột CreatedAt (bảng liên kết thuần: Id/MaintenanceId/
            // UserId/AssignedAt) ⇒ dùng CURRENT_TIMESTAMP, đúng bằng DEFAULT của chính cột AssignedAt.
            migrationBuilder.Sql("""
                UPDATE "asset_maintenance_assignees" SET "AssignedAt" = CURRENT_TIMESTAMP
                    WHERE "AssignedAt" IN ('-infinity'::timestamp, 'infinity'::timestamp);
                """);
            migrationBuilder.Sql("""
                UPDATE "asset_maintenances" SET "CreatedAt" =
                    CASE WHEN "CreatedAt" IN ('-infinity'::timestamp, 'infinity'::timestamp)
                         THEN CURRENT_TIMESTAMP ELSE "CreatedAt" END
                    WHERE "CreatedAt" IN ('-infinity'::timestamp, 'infinity'::timestamp);
                """);
            migrationBuilder.Sql("""
                UPDATE "asset_maintenances" SET "StartDate" =
                    CASE WHEN "CreatedAt" IN ('-infinity'::timestamp, 'infinity'::timestamp)
                         THEN CURRENT_TIMESTAMP ELSE "CreatedAt" END
                    WHERE "StartDate" IN ('-infinity'::timestamp, 'infinity'::timestamp);
                """);
            migrationBuilder.Sql("""
                UPDATE "asset_maintenances" SET "UpdatedAt" =
                    CASE WHEN "CreatedAt" IN ('-infinity'::timestamp, 'infinity'::timestamp)
                         THEN CURRENT_TIMESTAMP ELSE "CreatedAt" END
                    WHERE "UpdatedAt" IN ('-infinity'::timestamp, 'infinity'::timestamp);
                """);
            migrationBuilder.Sql("""
                UPDATE "component_units" SET "CreatedAt" =
                    CASE WHEN "CreatedAt" IN ('-infinity'::timestamp, 'infinity'::timestamp)
                         THEN CURRENT_TIMESTAMP ELSE "CreatedAt" END
                    WHERE "CreatedAt" IN ('-infinity'::timestamp, 'infinity'::timestamp);
                """);
            migrationBuilder.Sql("""
                UPDATE "component_units" SET "UpdatedAt" =
                    CASE WHEN "CreatedAt" IN ('-infinity'::timestamp, 'infinity'::timestamp)
                         THEN CURRENT_TIMESTAMP ELSE "CreatedAt" END
                    WHERE "UpdatedAt" IN ('-infinity'::timestamp, 'infinity'::timestamp);
                """);
            migrationBuilder.Sql("""
                UPDATE "license_seats" SET "CreatedAt" =
                    CASE WHEN "CreatedAt" IN ('-infinity'::timestamp, 'infinity'::timestamp)
                         THEN CURRENT_TIMESTAMP ELSE "CreatedAt" END
                    WHERE "CreatedAt" IN ('-infinity'::timestamp, 'infinity'::timestamp);
                """);
            migrationBuilder.Sql("""
                UPDATE "license_seats" SET "UpdatedAt" =
                    CASE WHEN "CreatedAt" IN ('-infinity'::timestamp, 'infinity'::timestamp)
                         THEN CURRENT_TIMESTAMP ELSE "CreatedAt" END
                    WHERE "UpdatedAt" IN ('-infinity'::timestamp, 'infinity'::timestamp);
                """);

            // ==================== [MT-TZ] Bước 2: chặn vĩnh viễn ±infinity trên 16 cột naive ====================
            migrationBuilder.AddCheckConstraint(
                name: "CK_licenses_DeletedAt_no_infinity",
                table: "licenses",
                sql: "\"DeletedAt\" <> '-infinity'::timestamp AND \"DeletedAt\" <> 'infinity'::timestamp");

            migrationBuilder.AddCheckConstraint(
                name: "CK_licenses_TerminationDate_no_infinity",
                table: "licenses",
                sql: "\"TerminationDate\" <> '-infinity'::timestamp AND \"TerminationDate\" <> 'infinity'::timestamp");

            migrationBuilder.AddCheckConstraint(
                name: "CK_license_seats_CreatedAt_no_infinity",
                table: "license_seats",
                sql: "\"CreatedAt\" <> '-infinity'::timestamp AND \"CreatedAt\" <> 'infinity'::timestamp");

            migrationBuilder.AddCheckConstraint(
                name: "CK_license_seats_UpdatedAt_no_infinity",
                table: "license_seats",
                sql: "\"UpdatedAt\" <> '-infinity'::timestamp AND \"UpdatedAt\" <> 'infinity'::timestamp");

            migrationBuilder.AddCheckConstraint(
                name: "CK_component_units_CreatedAt_no_infinity",
                table: "component_units",
                sql: "\"CreatedAt\" <> '-infinity'::timestamp AND \"CreatedAt\" <> 'infinity'::timestamp");

            migrationBuilder.AddCheckConstraint(
                name: "CK_component_units_DeletedAt_no_infinity",
                table: "component_units",
                sql: "\"DeletedAt\" <> '-infinity'::timestamp AND \"DeletedAt\" <> 'infinity'::timestamp");

            migrationBuilder.AddCheckConstraint(
                name: "CK_component_units_UpdatedAt_no_infinity",
                table: "component_units",
                sql: "\"UpdatedAt\" <> '-infinity'::timestamp AND \"UpdatedAt\" <> 'infinity'::timestamp");

            migrationBuilder.AddCheckConstraint(
                name: "CK_asset_maintenances_ClosedAt_no_infinity",
                table: "asset_maintenances",
                sql: "\"ClosedAt\" <> '-infinity'::timestamp AND \"ClosedAt\" <> 'infinity'::timestamp");

            migrationBuilder.AddCheckConstraint(
                name: "CK_asset_maintenances_CompletionDate_no_infinity",
                table: "asset_maintenances",
                sql: "\"CompletionDate\" <> '-infinity'::timestamp AND \"CompletionDate\" <> 'infinity'::timestamp");

            migrationBuilder.AddCheckConstraint(
                name: "CK_asset_maintenances_CreatedAt_no_infinity",
                table: "asset_maintenances",
                sql: "\"CreatedAt\" <> '-infinity'::timestamp AND \"CreatedAt\" <> 'infinity'::timestamp");

            migrationBuilder.AddCheckConstraint(
                name: "CK_asset_maintenances_DeletedAt_no_infinity",
                table: "asset_maintenances",
                sql: "\"DeletedAt\" <> '-infinity'::timestamp AND \"DeletedAt\" <> 'infinity'::timestamp");

            migrationBuilder.AddCheckConstraint(
                name: "CK_asset_maintenances_InspectedAt_no_infinity",
                table: "asset_maintenances",
                sql: "\"InspectedAt\" <> '-infinity'::timestamp AND \"InspectedAt\" <> 'infinity'::timestamp");

            migrationBuilder.AddCheckConstraint(
                name: "CK_asset_maintenances_StartDate_no_infinity",
                table: "asset_maintenances",
                sql: "\"StartDate\" <> '-infinity'::timestamp AND \"StartDate\" <> 'infinity'::timestamp");

            migrationBuilder.AddCheckConstraint(
                name: "CK_asset_maintenances_UpdatedAt_no_infinity",
                table: "asset_maintenances",
                sql: "\"UpdatedAt\" <> '-infinity'::timestamp AND \"UpdatedAt\" <> 'infinity'::timestamp");

            migrationBuilder.AddCheckConstraint(
                name: "CK_asset_maintenance_assignees_AssignedAt_no_infinity",
                table: "asset_maintenance_assignees",
                sql: "\"AssignedAt\" <> '-infinity'::timestamp AND \"AssignedAt\" <> 'infinity'::timestamp");

            migrationBuilder.AddCheckConstraint(
                name: "CK_action_logs_DeletedAt_no_infinity",
                table: "action_logs",
                sql: "\"DeletedAt\" <> '-infinity'::timestamp AND \"DeletedAt\" <> 'infinity'::timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // [MT-TZ] Việc dọn ±infinity ở Up() KHÔNG thể đảo ngược (không biết dòng nào từng là vô cực —
            // quy ước dọn ghi NULL / CreatedAt nên thông tin gốc đã mất). Rollback chỉ gỡ 16 CHECK.
            migrationBuilder.DropCheckConstraint(
                name: "CK_licenses_DeletedAt_no_infinity",
                table: "licenses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_licenses_TerminationDate_no_infinity",
                table: "licenses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_license_seats_CreatedAt_no_infinity",
                table: "license_seats");

            migrationBuilder.DropCheckConstraint(
                name: "CK_license_seats_UpdatedAt_no_infinity",
                table: "license_seats");

            migrationBuilder.DropCheckConstraint(
                name: "CK_component_units_CreatedAt_no_infinity",
                table: "component_units");

            migrationBuilder.DropCheckConstraint(
                name: "CK_component_units_DeletedAt_no_infinity",
                table: "component_units");

            migrationBuilder.DropCheckConstraint(
                name: "CK_component_units_UpdatedAt_no_infinity",
                table: "component_units");

            migrationBuilder.DropCheckConstraint(
                name: "CK_asset_maintenances_ClosedAt_no_infinity",
                table: "asset_maintenances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_asset_maintenances_CompletionDate_no_infinity",
                table: "asset_maintenances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_asset_maintenances_CreatedAt_no_infinity",
                table: "asset_maintenances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_asset_maintenances_DeletedAt_no_infinity",
                table: "asset_maintenances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_asset_maintenances_InspectedAt_no_infinity",
                table: "asset_maintenances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_asset_maintenances_StartDate_no_infinity",
                table: "asset_maintenances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_asset_maintenances_UpdatedAt_no_infinity",
                table: "asset_maintenances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_asset_maintenance_assignees_AssignedAt_no_infinity",
                table: "asset_maintenance_assignees");

            migrationBuilder.DropCheckConstraint(
                name: "CK_action_logs_DeletedAt_no_infinity",
                table: "action_logs");
        }
    }
}
