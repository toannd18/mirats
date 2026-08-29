using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace aspirereact.Server.Migrations
{
    /// <inheritdoc />
    public partial class StructuredThresholdOnStandardParam : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // [MC-10] ThresholdValue: text tự do ("80%") → numeric(18,4). Postgres KHÔNG implicit-cast
            // text→numeric nên cần USING; chuỗi không phải số (hoặc rỗng) → 0 (dữ liệu thực tế đang NULL).
            migrationBuilder.Sql("UPDATE maintenance_standard_params SET \"ThresholdValue\" = NULL WHERE \"ThresholdValue\" = ''");
            migrationBuilder.Sql("ALTER TABLE maintenance_standard_params ALTER COLUMN \"ThresholdValue\" TYPE numeric(18,4) USING CASE WHEN \"ThresholdValue\" ~ '^-?[0-9]+([.,][0-9]+)?$' THEN REPLACE(\"ThresholdValue\", ',', '.')::numeric(18,4) ELSE 0 END;");
            migrationBuilder.Sql("ALTER TABLE maintenance_standard_params ALTER COLUMN \"ThresholdValue\" SET NOT NULL");
            migrationBuilder.Sql("ALTER TABLE maintenance_standard_params ALTER COLUMN \"ThresholdValue\" SET DEFAULT 0");

            migrationBuilder.AddColumn<int>(
                name: "ThresholdOperator",
                table: "maintenance_standard_params",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThresholdOperator",
                table: "maintenance_standard_params");

            migrationBuilder.Sql("ALTER TABLE maintenance_standard_params ALTER COLUMN \"ThresholdValue\" DROP DEFAULT");
            migrationBuilder.Sql("ALTER TABLE maintenance_standard_params ALTER COLUMN \"ThresholdValue\" TYPE text");
            migrationBuilder.Sql("ALTER TABLE maintenance_standard_params ALTER COLUMN \"ThresholdValue\" DROP NOT NULL");
        }
    }
}
