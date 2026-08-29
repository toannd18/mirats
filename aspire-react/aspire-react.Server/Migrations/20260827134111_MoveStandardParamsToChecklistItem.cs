using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace aspirereact.Server.Migrations
{
    /// <inheritdoc />
    public partial class MoveStandardParamsToChecklistItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_maintenance_standard_params_maintenance_checklist_template_~",
                table: "maintenance_standard_params");

            migrationBuilder.RenameColumn(
                name: "TemplateVersionId",
                table: "maintenance_standard_params",
                newName: "ChecklistItemId");

            migrationBuilder.RenameIndex(
                name: "IX_maintenance_standard_params_TemplateVersionId",
                table: "maintenance_standard_params",
                newName: "IX_maintenance_standard_params_ChecklistItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_maintenance_standard_params_maintenance_checklist_items_Che~",
                table: "maintenance_standard_params",
                column: "ChecklistItemId",
                principalTable: "maintenance_checklist_items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_maintenance_standard_params_maintenance_checklist_items_Che~",
                table: "maintenance_standard_params");

            migrationBuilder.RenameColumn(
                name: "ChecklistItemId",
                table: "maintenance_standard_params",
                newName: "TemplateVersionId");

            migrationBuilder.RenameIndex(
                name: "IX_maintenance_standard_params_ChecklistItemId",
                table: "maintenance_standard_params",
                newName: "IX_maintenance_standard_params_TemplateVersionId");

            migrationBuilder.AddForeignKey(
                name: "FK_maintenance_standard_params_maintenance_checklist_template_~",
                table: "maintenance_standard_params",
                column: "TemplateVersionId",
                principalTable: "maintenance_checklist_template_versions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
