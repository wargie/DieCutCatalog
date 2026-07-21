using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DieCutCatalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDieCutMileageAndEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "JcOrderNumber",
                table: "die_cuts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Mileage",
                table: "die_cuts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "die_cut_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DieCutId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Quantity = table.Column<long>(type: "bigint", nullable: true),
                    MileageBefore = table.Column<long>(type: "bigint", nullable: false),
                    MileageAfter = table.Column<long>(type: "bigint", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_die_cut_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_die_cut_events_die_cuts_DieCutId",
                        column: x => x.DieCutId,
                        principalTable: "die_cuts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_die_cut_events_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_die_cut_events_DieCutId_OccurredAt",
                table: "die_cut_events",
                columns: new[] { "DieCutId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_die_cut_events_EmployeeId",
                table: "die_cut_events",
                column: "EmployeeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "die_cut_events");

            migrationBuilder.DropColumn(
                name: "JcOrderNumber",
                table: "die_cuts");

            migrationBuilder.DropColumn(
                name: "Mileage",
                table: "die_cuts");
        }
    }
}
