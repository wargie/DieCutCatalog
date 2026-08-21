using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DieCutCatalog.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CatalogDbContext))]
[Migration("20260821100000_NormalizeInspectionResourceTo500000")]
public partial class NormalizeInspectionResourceTo500000 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE die_cuts
            SET "Mileage" = 0,
                "RunLengthMeters" = 0,
                "Revolutions" = 0,
                "NextInspectionRevolutions" = 500000;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Current counters cannot be reconstructed; lifetime counters and event history remain intact.
    }
}
