using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DieCutCatalog.Infrastructure.Persistence.Migrations;

public partial class SetInspectionIntervalTo500000 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE "DieCuts"
            SET "NextInspectionRevolutions" = GREATEST(500000, "NextInspectionRevolutions" - 500000);

            UPDATE "DieCuts"
            SET "Status" = 'NeedsInspection'
            WHERE "Status" = 'Active'
              AND "Revolutions" >= "NextInspectionRevolutions";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE "DieCuts"
            SET "NextInspectionRevolutions" = "NextInspectionRevolutions" + 500000;
            """);
    }
}
