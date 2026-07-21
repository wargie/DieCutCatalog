using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DieCutCatalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDieCutCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "equipment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_equipment", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "die_cuts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NormalizedNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShaftRepeatMm = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    WidthMm = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    LengthMm = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    Streams = table.Column<int>(type: "integer", nullable: false),
                    Repeats = table.Column<int>(type: "integer", nullable: false),
                    GapAcrossMm = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    GapAlongMm = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    Material = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MaterialWidthMm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    KnifeHeightMicrons = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    Shape = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Comments = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CommissionedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_die_cuts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_die_cuts_equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_die_cuts_EquipmentId_NormalizedNumber",
                table: "die_cuts",
                columns: new[] { "EquipmentId", "NormalizedNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_die_cuts_Material",
                table: "die_cuts",
                column: "Material");

            migrationBuilder.CreateIndex(
                name: "IX_die_cuts_Status",
                table: "die_cuts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_NormalizedName",
                table: "equipment",
                column: "NormalizedName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "die_cuts");

            migrationBuilder.DropTable(
                name: "equipment");
        }
    }
}
