using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DieCutCatalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlignDieCutFieldsWithExcel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(name: "ShaftRepeatMm", table: "die_cuts", newName: "Shaft");
            migrationBuilder.RenameColumn(name: "WidthMm", table: "die_cuts", newName: "X");
            migrationBuilder.RenameColumn(name: "LengthMm", table: "die_cuts", newName: "Y");
            migrationBuilder.RenameColumn(name: "GapAcrossMm", table: "die_cuts", newName: "GapX");
            migrationBuilder.RenameColumn(name: "GapAlongMm", table: "die_cuts", newName: "GapY");
            migrationBuilder.RenameColumn(name: "MaterialWidthMm", table: "die_cuts", newName: "H");
            migrationBuilder.RenameColumn(name: "Shape", table: "die_cuts", newName: "Figure");
            migrationBuilder.RenameColumn(name: "CommissionedOn", table: "die_cuts", newName: "Date");

            migrationBuilder.AlterColumn<decimal>(
                name: "GapX",
                table: "die_cuts",
                type: "numeric(14,9)",
                precision: 14,
                scale: 9,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,3)",
                oldPrecision: 10,
                oldScale: 3);

            migrationBuilder.AlterColumn<decimal>(
                name: "GapY",
                table: "die_cuts",
                type: "numeric(14,9)",
                precision: 14,
                scale: 9,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,3)",
                oldPrecision: 10,
                oldScale: 3);

            migrationBuilder.Sql("UPDATE die_cuts SET \"GapX\" = \"GapX\" / 1000.0, \"GapY\" = \"GapY\" / 1000.0;");

            migrationBuilder.DropColumn(name: "KnifeHeightMicrons", table: "die_cuts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "KnifeHeightMicrons",
                table: "die_cuts",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.Sql("UPDATE die_cuts SET \"GapX\" = \"GapX\" * 1000.0, \"GapY\" = \"GapY\" * 1000.0;");

            migrationBuilder.AlterColumn<decimal>(
                name: "GapX",
                table: "die_cuts",
                type: "numeric(10,3)",
                precision: 10,
                scale: 3,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(14,9)",
                oldPrecision: 14,
                oldScale: 9);

            migrationBuilder.AlterColumn<decimal>(
                name: "GapY",
                table: "die_cuts",
                type: "numeric(10,3)",
                precision: 10,
                scale: 3,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(14,9)",
                oldPrecision: 14,
                oldScale: 9);

            migrationBuilder.RenameColumn(name: "Shaft", table: "die_cuts", newName: "ShaftRepeatMm");
            migrationBuilder.RenameColumn(name: "X", table: "die_cuts", newName: "WidthMm");
            migrationBuilder.RenameColumn(name: "Y", table: "die_cuts", newName: "LengthMm");
            migrationBuilder.RenameColumn(name: "GapX", table: "die_cuts", newName: "GapAcrossMm");
            migrationBuilder.RenameColumn(name: "GapY", table: "die_cuts", newName: "GapAlongMm");
            migrationBuilder.RenameColumn(name: "H", table: "die_cuts", newName: "MaterialWidthMm");
            migrationBuilder.RenameColumn(name: "Figure", table: "die_cuts", newName: "Shape");
            migrationBuilder.RenameColumn(name: "Date", table: "die_cuts", newName: "CommissionedOn");
        }
    }
}
