using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DieCutCatalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRunLengthAndRevolutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Revolutions",
                table: "die_cuts",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RunLengthMeters",
                table: "die_cuts",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RevolutionsAfter",
                table: "die_cut_events",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RevolutionsBefore",
                table: "die_cut_events",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RunLengthMetersAfter",
                table: "die_cut_events",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RunLengthMetersBefore",
                table: "die_cut_events",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql(
                """
                UPDATE "die_cuts"
                SET "RunLengthMeters" = ROUND(
                        ("Mileage"::numeric / NULLIF("Streams", 0)) * ("Y" / 1000.0 + "GapY"),
                        6),
                    "Revolutions" = ROUND(
                        (("Mileage"::numeric / NULLIF("Streams", 0)) * ("Y" / 1000.0 + "GapY"))
                        / NULLIF("Shaft" * 3.175 / 1000.0, 0),
                        6);

                UPDATE "die_cut_events" AS event
                SET "RunLengthMetersBefore" = ROUND(
                        (event."MileageBefore"::numeric / NULLIF(die_cut."Streams", 0))
                        * (die_cut."Y" / 1000.0 + die_cut."GapY"),
                        6),
                    "RunLengthMetersAfter" = ROUND(
                        (event."MileageAfter"::numeric / NULLIF(die_cut."Streams", 0))
                        * (die_cut."Y" / 1000.0 + die_cut."GapY"),
                        6),
                    "RevolutionsBefore" = ROUND(
                        ((event."MileageBefore"::numeric / NULLIF(die_cut."Streams", 0))
                        * (die_cut."Y" / 1000.0 + die_cut."GapY"))
                        / NULLIF(die_cut."Shaft" * 3.175 / 1000.0, 0),
                        6),
                    "RevolutionsAfter" = ROUND(
                        ((event."MileageAfter"::numeric / NULLIF(die_cut."Streams", 0))
                        * (die_cut."Y" / 1000.0 + die_cut."GapY"))
                        / NULLIF(die_cut."Shaft" * 3.175 / 1000.0, 0),
                        6)
                FROM "die_cuts" AS die_cut
                WHERE event."DieCutId" = die_cut."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Revolutions",
                table: "die_cuts");

            migrationBuilder.DropColumn(
                name: "RunLengthMeters",
                table: "die_cuts");

            migrationBuilder.DropColumn(
                name: "RevolutionsAfter",
                table: "die_cut_events");

            migrationBuilder.DropColumn(
                name: "RevolutionsBefore",
                table: "die_cut_events");

            migrationBuilder.DropColumn(
                name: "RunLengthMetersAfter",
                table: "die_cut_events");

            migrationBuilder.DropColumn(
                name: "RunLengthMetersBefore",
                table: "die_cut_events");
        }
    }
}
