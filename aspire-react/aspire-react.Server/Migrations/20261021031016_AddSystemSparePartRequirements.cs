using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace aspirereact.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemSparePartRequirements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "system_spare_part_requirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    SystemInfoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComponentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConsumableId = table.Column<Guid>(type: "uuid", nullable: true),
                    MinQty = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "true"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_spare_part_requirements", x => x.Id);
                    table.CheckConstraint("CK_system_spare_part_requirements_min_qty", "\"MinQty\" >= 0");
                    table.CheckConstraint("CK_system_spare_part_requirements_one_item", "((\"ComponentId\" IS NOT NULL)::int + (\"ConsumableId\" IS NOT NULL)::int) = 1");
                    table.ForeignKey(
                        name: "FK_system_spare_part_requirements_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_system_spare_part_requirements_components_ComponentId",
                        column: x => x.ComponentId,
                        principalTable: "components",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_spare_part_requirements_consumables_ConsumableId",
                        column: x => x.ConsumableId,
                        principalTable: "consumables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_spare_part_requirements_system_infos_SystemInfoId",
                        column: x => x.SystemInfoId,
                        principalTable: "system_infos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_system_spare_part_requirements_CompanyId",
                table: "system_spare_part_requirements",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_system_spare_part_requirements_ComponentId",
                table: "system_spare_part_requirements",
                column: "ComponentId");

            migrationBuilder.CreateIndex(
                name: "IX_system_spare_part_requirements_ConsumableId",
                table: "system_spare_part_requirements",
                column: "ConsumableId");

            migrationBuilder.CreateIndex(
                name: "UX_spare_requirements_system_component",
                table: "system_spare_part_requirements",
                columns: new[] { "SystemInfoId", "ComponentId" },
                unique: true,
                filter: "\"ComponentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_spare_requirements_system_consumable",
                table: "system_spare_part_requirements",
                columns: new[] { "SystemInfoId", "ConsumableId" },
                unique: true,
                filter: "\"ConsumableId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "system_spare_part_requirements");
        }
    }
}
