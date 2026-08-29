using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace aspirereact.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddStandardParamIdToChecklistResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_maintenance_checklist_results_CampaignId_DeviceSnapshotId_C~",
                table: "maintenance_checklist_results");

            migrationBuilder.AddColumn<Guid>(
                name: "StandardParamId",
                table: "maintenance_checklist_results",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_checklist_results_CampaignId_DeviceSnapshotId_~1",
                table: "maintenance_checklist_results",
                columns: new[] { "CampaignId", "DeviceSnapshotId", "ChecklistItemId", "StandardParamId" },
                unique: true,
                filter: "\"StandardParamId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_checklist_results_CampaignId_DeviceSnapshotId_C~",
                table: "maintenance_checklist_results",
                columns: new[] { "CampaignId", "DeviceSnapshotId", "ChecklistItemId" },
                unique: true,
                filter: "\"StandardParamId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_checklist_results_StandardParamId",
                table: "maintenance_checklist_results",
                column: "StandardParamId");

            migrationBuilder.AddForeignKey(
                name: "FK_maintenance_checklist_results_maintenance_standard_params_S~",
                table: "maintenance_checklist_results",
                column: "StandardParamId",
                principalTable: "maintenance_standard_params",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_maintenance_checklist_results_maintenance_standard_params_S~",
                table: "maintenance_checklist_results");

            migrationBuilder.DropIndex(
                name: "IX_maintenance_checklist_results_CampaignId_DeviceSnapshotId_~1",
                table: "maintenance_checklist_results");

            migrationBuilder.DropIndex(
                name: "IX_maintenance_checklist_results_CampaignId_DeviceSnapshotId_C~",
                table: "maintenance_checklist_results");

            migrationBuilder.DropIndex(
                name: "IX_maintenance_checklist_results_StandardParamId",
                table: "maintenance_checklist_results");

            migrationBuilder.DropColumn(
                name: "StandardParamId",
                table: "maintenance_checklist_results");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_checklist_results_CampaignId_DeviceSnapshotId_C~",
                table: "maintenance_checklist_results",
                columns: new[] { "CampaignId", "DeviceSnapshotId", "ChecklistItemId" },
                unique: true);
        }
    }
}
