using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DieCutCatalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKnifeResourceLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Generation",
                table: "die_cuts",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<long>(
                name: "LifetimeMileage",
                table: "die_cuts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "LifetimeRevolutions",
                table: "die_cuts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<decimal>(
                name: "LifetimeRunLengthMeters",
                table: "die_cuts",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "NextInspectionRevolutions",
                table: "die_cuts",
                type: "bigint",
                nullable: false,
                defaultValue: 1000000L);

            migrationBuilder.Sql("""
                UPDATE die_cuts AS d
                SET "Generation" = 1,
                    "LifetimeMileage" = GREATEST(
                        d."Mileage",
                        COALESCE((
                            SELECT SUM(e."Quantity")
                            FROM die_cut_events AS e
                            WHERE e."DieCutId" = d."Id" AND e."Type" = 'CirculationAdded'
                        ), 0)),
                    "LifetimeRunLengthMeters" = GREATEST(
                        d."RunLengthMeters",
                        COALESCE((
                            SELECT SUM(GREATEST(e."RunLengthMetersAfter" - e."RunLengthMetersBefore", 0))
                            FROM die_cut_events AS e
                            WHERE e."DieCutId" = d."Id" AND e."Type" = 'CirculationAdded'
                        ), 0)),
                    "LifetimeRevolutions" = GREATEST(
                        d."Revolutions",
                        COALESCE((
                            SELECT SUM(GREATEST(e."RevolutionsAfter" - e."RevolutionsBefore", 0))
                            FROM die_cut_events AS e
                            WHERE e."DieCutId" = d."Id" AND e."Type" = 'CirculationAdded'
                        ), 0)),
                    "NextInspectionRevolutions" = 1000000;

                UPDATE die_cuts
                SET "Status" = 'NeedsInspection'
                WHERE "Status" = 'Active' AND "Revolutions" >= 1000000;
                """);        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Generation",
                table: "die_cuts");

            migrationBuilder.DropColumn(
                name: "LifetimeMileage",
                table: "die_cuts");

            migrationBuilder.DropColumn(
                name: "LifetimeRevolutions",
                table: "die_cuts");

            migrationBuilder.DropColumn(
                name: "LifetimeRunLengthMeters",
                table: "die_cuts");

            migrationBuilder.DropColumn(
                name: "NextInspectionRevolutions",
                table: "die_cuts");
        }
    }
}
