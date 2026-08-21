using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace aspirereact.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "companies",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            // [Task ASSET-TAG-AUTO] Backfill Code for pre-existing companies. A base code is derived
            // from the name (letters only, uppercased, up to 4 chars; fallback 'CO'), then a numeric
            // suffix is appended on collision so every code is unique. 'NOCO' is reserved for floaters.
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    rec RECORD;
                    base TEXT;
                    cand TEXT;
                    suff INT;
                BEGIN
                    FOR rec IN SELECT ""Id"", ""Name"" FROM public.""companies"" ORDER BY ""Name"", ""Id"" LOOP
                        base := UPPER(REGEXP_REPLACE(rec.""Name"", '[^A-Za-z]', '', 'g'));
                        IF base = '' THEN base := 'CO'; END IF;
                        IF LENGTH(base) > 4 THEN base := LEFT(base, 4); END IF;

                        cand := base;
                        suff := 2;
                        LOOP
                            IF EXISTS (SELECT 1 FROM public.""companies"" WHERE ""Code"" = cand AND ""Id"" <> rec.""Id"")
                               OR cand = 'NOCO' THEN
                                cand := LEFT(base, GREATEST(0, 4 - LENGTH(suff::text))) || suff::text;
                                IF cand = 'NOCO' THEN cand := base || suff::text; END IF;
                                suff := suff + 1;
                                CONTINUE;
                            END IF;
                            EXIT;
                        END LOOP;

                        UPDATE public.""companies"" SET ""Code"" = cand WHERE ""Id"" = rec.""Id"";
                    END LOOP;
                END $$;");

            migrationBuilder.CreateIndex(
                name: "IX_companies_Code",
                table: "companies",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_companies_Code",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "companies");
        }
    }
}
