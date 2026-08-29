using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace aspirereact.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceChecklistItemPositions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "maintenance_checklist_item_positions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    SystemPositionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_checklist_item_positions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_maintenance_checklist_item_positions_maintenance_checklist_~",
                        column: x => x.ItemId,
                        principalTable: "maintenance_checklist_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_maintenance_checklist_item_positions_system_positions_Syste~",
                        column: x => x.SystemPositionId,
                        principalTable: "system_positions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_checklist_item_positions_ItemId_SystemPositionId",
                table: "maintenance_checklist_item_positions",
                columns: new[] { "ItemId", "SystemPositionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_checklist_item_positions_SystemPositionId",
                table: "maintenance_checklist_item_positions",
                column: "SystemPositionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "maintenance_checklist_item_positions");
        }
    }
}
