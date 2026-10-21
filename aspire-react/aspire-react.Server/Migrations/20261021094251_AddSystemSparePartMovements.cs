using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace aspirereact.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemSparePartMovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "system_spare_part_movements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    SystemInfoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComponentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConsumableId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_spare_part_movements", x => x.Id);
                    table.CheckConstraint("CK_system_spare_part_movements_one_item", "((\"ComponentId\" IS NOT NULL)::int + (\"ConsumableId\" IS NOT NULL)::int) = 1");
                    table.CheckConstraint("CK_system_spare_part_movements_quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_system_spare_part_movements_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_system_spare_part_movements_components_ComponentId",
                        column: x => x.ComponentId,
                        principalTable: "components",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_spare_part_movements_consumables_ConsumableId",
                        column: x => x.ConsumableId,
                        principalTable: "consumables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_spare_part_movements_system_infos_SystemInfoId",
                        column: x => x.SystemInfoId,
                        principalTable: "system_infos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_spare_part_movements_users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_spare_movements_component",
                table: "system_spare_part_movements",
                column: "ComponentId");

            migrationBuilder.CreateIndex(
                name: "IX_spare_movements_consumable",
                table: "system_spare_part_movements",
                column: "ConsumableId");

            migrationBuilder.CreateIndex(
                name: "IX_spare_movements_system_created",
                table: "system_spare_part_movements",
                columns: new[] { "SystemInfoId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_system_spare_part_movements_CompanyId",
                table: "system_spare_part_movements",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_system_spare_part_movements_CreatedById",
                table: "system_spare_part_movements",
                column: "CreatedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "system_spare_part_movements");
        }
    }
}
