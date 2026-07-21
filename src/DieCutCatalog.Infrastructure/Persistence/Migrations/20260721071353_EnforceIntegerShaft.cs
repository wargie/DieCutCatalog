using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DieCutCatalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceIntegerShaft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rows created before and after the Excel-alignment migration can use different shaft units.
            migrationBuilder.Sql(
                """
                ALTER TABLE die_cuts
                ALTER COLUMN "Shaft" TYPE integer
                USING ROUND(
                    CASE
                        WHEN ABS((("Shaft" / NULLIF("Repeats", 0)) - "Y") / 1000.0 - "GapY")
                           <= ABS((("Shaft" * 3.175 / NULLIF("Repeats", 0)) - "Y") / 1000.0 - "GapY")
                        THEN "Shaft" / 3.175
                        ELSE "Shaft"
                    END
                )::integer;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE die_cuts
                ALTER COLUMN "Shaft" TYPE numeric(10,3)
                USING "Shaft"::numeric(10,3);
                """);
        }
    }
}
