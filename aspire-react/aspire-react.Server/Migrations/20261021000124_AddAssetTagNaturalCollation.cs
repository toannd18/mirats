using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace aspirereact.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetTagNaturalCollation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // UI-1 GD3 - natural asset-tag ordering: ICU numeric collation so "-2" < "-10".
            migrationBuilder.Sql("""
                CREATE COLLATION IF NOT EXISTS asset_tag_natural (provider = icu, locale = 'und-u-kn-true');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP COLLATION IF EXISTS asset_tag_natural;");
        }
    }
}
