using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace aspirereact.Server.Migrations
{
    /// <inheritdoc />
    public partial class LicenseSeatSystemInfoTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_license_seats_system_positions_SystemPositionId",
                table: "license_seats");

            migrationBuilder.DropCheckConstraint(
                name: "CK_license_seats_single_target",
                table: "license_seats");

            migrationBuilder.RenameColumn(
                name: "SystemPositionId",
                table: "license_seats",
                newName: "SystemInfoId");

            migrationBuilder.RenameIndex(
                name: "IX_license_seats_SystemPositionId",
                table: "license_seats",
                newName: "IX_license_seats_SystemInfoId");

            // Backfill: the renamed column still holds OLD SystemPositionId (child) values.
            // Remap each to its parent SystemInfoId (a license applies to the whole system,
            // never a specific position). All rows are guaranteed resolvable — no orphan positions.
            migrationBuilder.Sql(@"
                UPDATE ""license_seats"" ls
                SET ""SystemInfoId"" = sp.""SystemInfoId""
                FROM ""system_positions"" sp
                WHERE sp.""Id"" = ls.""SystemInfoId"" AND ls.""SystemInfoId"" IS NOT NULL;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_license_seats_single_target",
                table: "license_seats",
                sql: "(((\"UserId\" IS NOT NULL)::int + (\"AssetId\" IS NOT NULL)::int + (\"SystemInfoId\" IS NOT NULL)::int) <= 1)");

            migrationBuilder.AddForeignKey(
                name: "FK_license_seats_system_infos_SystemInfoId",
                table: "license_seats",
                column: "SystemInfoId",
                principalTable: "system_infos",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_license_seats_system_infos_SystemInfoId",
                table: "license_seats");

            migrationBuilder.DropCheckConstraint(
                name: "CK_license_seats_single_target",
                table: "license_seats");

            migrationBuilder.RenameColumn(
                name: "SystemInfoId",
                table: "license_seats",
                newName: "SystemPositionId");

            migrationBuilder.RenameIndex(
                name: "IX_license_seats_SystemInfoId",
                table: "license_seats",
                newName: "IX_license_seats_SystemPositionId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_license_seats_single_target",
                table: "license_seats",
                sql: "(((\"UserId\" IS NOT NULL)::int + (\"AssetId\" IS NOT NULL)::int + (\"SystemPositionId\" IS NOT NULL)::int) <= 1)");

            migrationBuilder.AddForeignKey(
                name: "FK_license_seats_system_positions_SystemPositionId",
                table: "license_seats",
                column: "SystemPositionId",
                principalTable: "system_positions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
