using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace aspirereact.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceChecklist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NextMaintenanceDueDate",
                table: "system_infos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "maintenance_checklist_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SystemInfoId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_checklist_templates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_maintenance_checklist_templates_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_maintenance_checklist_templates_system_infos_SystemInfoId",
                        column: x => x.SystemInfoId,
                        principalTable: "system_infos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_checklist_template_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_checklist_template_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_maintenance_checklist_template_versions_maintenance_checkli~",
                        column: x => x.TemplateId,
                        principalTable: "maintenance_checklist_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SystemInfoId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BatchNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_campaigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_maintenance_campaigns_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_maintenance_campaigns_maintenance_checklist_template_versio~",
                        column: x => x.TemplateVersionId,
                        principalTable: "maintenance_checklist_template_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_maintenance_campaigns_system_infos_SystemInfoId",
                        column: x => x.SystemInfoId,
                        principalTable: "system_infos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_maintenance_campaigns_users_ReviewerId",
                        column: x => x.ReviewerId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_checklist_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TemplateVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CycleMonths = table.Column<int>(type: "integer", nullable: false),
                    ToolsRequired = table.Column<string>(type: "text", nullable: true),
                    Instruction = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_checklist_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_maintenance_checklist_items_maintenance_checklist_template_~",
                        column: x => x.TemplateVersionId,
                        principalTable: "maintenance_checklist_template_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_standard_params",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TemplateVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceCategoryOrType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ParamName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NominalValue = table.Column<string>(type: "text", nullable: true),
                    ThresholdValue = table.Column<string>(type: "text", nullable: true),
                    CheckMethod = table.Column<string>(type: "text", nullable: true),
                    Unit = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_standard_params", x => x.Id);
                    table.ForeignKey(
                        name: "FK_maintenance_standard_params_maintenance_checklist_template_~",
                        column: x => x.TemplateVersionId,
                        principalTable: "maintenance_checklist_template_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_campaign_device_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetTag = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    AssetName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Serial = table.Column<string>(type: "text", nullable: true),
                    ModelNumber = table.Column<string>(type: "text", nullable: true),
                    SystemPositionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SystemPositionName = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_campaign_device_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_maintenance_campaign_device_snapshots_maintenance_campaigns~",
                        column: x => x.CampaignId,
                        principalTable: "maintenance_campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_campaign_executors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_campaign_executors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_maintenance_campaign_executors_maintenance_campaigns_Campai~",
                        column: x => x.CampaignId,
                        principalTable: "maintenance_campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_maintenance_campaign_executors_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_checklist_results",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChecklistItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    MeasuredValue = table.Column<string>(type: "text", nullable: true),
                    IsPass = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_checklist_results", x => x.Id);
                    table.ForeignKey(
                        name: "FK_maintenance_checklist_results_maintenance_campaign_device_s~",
                        column: x => x.DeviceSnapshotId,
                        principalTable: "maintenance_campaign_device_snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_maintenance_checklist_results_maintenance_campaigns_Campaig~",
                        column: x => x.CampaignId,
                        principalTable: "maintenance_campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_maintenance_checklist_results_maintenance_checklist_items_C~",
                        column: x => x.ChecklistItemId,
                        principalTable: "maintenance_checklist_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_campaign_device_snapshots_CampaignId",
                table: "maintenance_campaign_device_snapshots",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_campaign_executors_CampaignId_UserId",
                table: "maintenance_campaign_executors",
                columns: new[] { "CampaignId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_campaign_executors_UserId",
                table: "maintenance_campaign_executors",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_campaigns_CompanyId",
                table: "maintenance_campaigns",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_campaigns_ReviewerId",
                table: "maintenance_campaigns",
                column: "ReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_campaigns_SystemInfoId",
                table: "maintenance_campaigns",
                column: "SystemInfoId");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_campaigns_TemplateVersionId",
                table: "maintenance_campaigns",
                column: "TemplateVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_checklist_items_TemplateVersionId_Order",
                table: "maintenance_checklist_items",
                columns: new[] { "TemplateVersionId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_checklist_results_CampaignId_DeviceSnapshotId_C~",
                table: "maintenance_checklist_results",
                columns: new[] { "CampaignId", "DeviceSnapshotId", "ChecklistItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_checklist_results_ChecklistItemId",
                table: "maintenance_checklist_results",
                column: "ChecklistItemId");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_checklist_results_DeviceSnapshotId",
                table: "maintenance_checklist_results",
                column: "DeviceSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_checklist_template_versions_TemplateId_IsCurrent",
                table: "maintenance_checklist_template_versions",
                columns: new[] { "TemplateId", "IsCurrent" },
                unique: true,
                filter: "\"IsCurrent\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_checklist_template_versions_TemplateId_VersionN~",
                table: "maintenance_checklist_template_versions",
                columns: new[] { "TemplateId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_checklist_templates_CompanyId",
                table: "maintenance_checklist_templates",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_checklist_templates_SystemInfoId_Name",
                table: "maintenance_checklist_templates",
                columns: new[] { "SystemInfoId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_standard_params_TemplateVersionId",
                table: "maintenance_standard_params",
                column: "TemplateVersionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "maintenance_campaign_executors");

            migrationBuilder.DropTable(
                name: "maintenance_checklist_results");

            migrationBuilder.DropTable(
                name: "maintenance_standard_params");

            migrationBuilder.DropTable(
                name: "maintenance_campaign_device_snapshots");

            migrationBuilder.DropTable(
                name: "maintenance_checklist_items");

            migrationBuilder.DropTable(
                name: "maintenance_campaigns");

            migrationBuilder.DropTable(
                name: "maintenance_checklist_template_versions");

            migrationBuilder.DropTable(
                name: "maintenance_checklist_templates");

            migrationBuilder.DropColumn(
                name: "NextMaintenanceDueDate",
                table: "system_infos");
        }
    }
}
