using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace aspirereact.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetInstallations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asset_installations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    SystemPositionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SystemPositionName = table.Column<string>(type: "text", nullable: true),
                    SystemInfoId = table.Column<Guid>(type: "uuid", nullable: true),
                    SystemInfoName = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SupersededAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CorrectsEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    LoggedById = table.Column<Guid>(type: "uuid", nullable: true),
                    LoggedByName = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_asset_installations", x => x.Id);
                    table.CheckConstraint("CK_asset_installations_ended_after_started", "\"EndedAt\" IS NULL OR \"EndedAt\" >= \"StartedAt\"");
                    table.ForeignKey(
                        name: "FK_asset_installations_asset_installations_CorrectsEntryId",
                        column: x => x.CorrectsEntryId,
                        principalTable: "asset_installations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_asset_installations_assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_asset_installations_system_positions_SystemPositionId",
                        column: x => x.SystemPositionId,
                        principalTable: "system_positions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_asset_installations_users_LoggedById",
                        column: x => x.LoggedById,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_asset_installations_AssetId_StartedAt",
                table: "asset_installations",
                columns: new[] { "AssetId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_asset_installations_CorrectsEntryId",
                table: "asset_installations",
                column: "CorrectsEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_asset_installations_LoggedById",
                table: "asset_installations",
                column: "LoggedById");

            migrationBuilder.CreateIndex(
                name: "IX_asset_installations_SystemPositionId",
                table: "asset_installations",
                column: "SystemPositionId");

            // [LL-1] Ràng buộc chống chồng khoảng thời gian — "tại một thời điểm, một Asset chỉ gắn
            // tối đa một vị trí". EF Core không biểu diễn được EXCLUDE constraint qua Fluent API nên
            // tạo bằng raw SQL. btree_gist cung cấp GiST operator class cho `uuid` (AssetId WITH =);
            // tstzrange(..., '[)') với EndedAt NULL = vô hạn → hai khoảng MỞ cùng asset bị từ chối.
            // Partial (SupersededAt IS NULL): chỉ các bản ĐANG hiệu lực mới phải không chồng nhau
            // (bản đã bị đính chính không tính). Idempotent — safe khi DB đã có extension.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
            migrationBuilder.Sql(
                "ALTER TABLE asset_installations " +
                "ADD CONSTRAINT \"EX_asset_installations_no_overlap\" " +
                "EXCLUDE USING gist (" +
                "\"AssetId\" WITH =, " +
                "tstzrange(\"StartedAt\", \"EndedAt\", '[)') WITH &&" +
                ") WHERE (\"SupersededAt\" IS NULL);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // DropTable cũng gỡ luôn EX_asset_installations_no_overlap. KHÔNG drop extension
            // btree_gist: đây là object dùng chung ở cấp DB (có thể đang được feature khác dùng),
            // tạo idempotent nên để lại là vô hại.
            migrationBuilder.DropTable(
                name: "asset_installations");
        }
    }
}
