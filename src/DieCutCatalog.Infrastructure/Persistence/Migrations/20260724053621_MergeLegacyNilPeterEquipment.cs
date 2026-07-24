using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DieCutCatalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MergeLegacyNilPeterEquipment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_die_cuts_EquipmentId_NormalizedNumber",
                table: "die_cuts");

            migrationBuilder.CreateIndex(
                name: "IX_die_cuts_EquipmentId_NormalizedNumber",
                table: "die_cuts",
                columns: new[] { "EquipmentId", "NormalizedNumber" },
                unique: true,
                filter: "\"Status\" <> 'Deleted'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_die_cuts_EquipmentId_NormalizedNumber",
                table: "die_cuts");

            migrationBuilder.CreateIndex(
                name: "IX_die_cuts_EquipmentId_NormalizedNumber",
                table: "die_cuts",
                columns: new[] { "EquipmentId", "NormalizedNumber" },
                unique: true);
        }
    }
}
