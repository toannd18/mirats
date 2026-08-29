using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace aspirereact.Server.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDeviceCategoryOrTypeFromStandardParams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeviceCategoryOrType",
                table: "maintenance_standard_params");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeviceCategoryOrType",
                table: "maintenance_standard_params",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }
    }
}
