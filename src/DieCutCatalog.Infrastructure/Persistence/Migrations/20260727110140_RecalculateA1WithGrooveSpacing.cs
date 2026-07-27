using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DieCutCatalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecalculateA1WithGrooveSpacing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE die_cuts
                SET "GapX" = (
                    "H"
                    - ("X" * "Streams")
                    - ("GrooveSpacing" * GREATEST("Streams" - 1, 0))
                ) / 1000.0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE die_cuts
                SET "GapX" = ("H" - ("X" * "Streams")) / 1000.0;
                """);
        }
    }
}