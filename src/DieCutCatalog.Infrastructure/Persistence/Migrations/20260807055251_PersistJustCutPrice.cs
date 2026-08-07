using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DieCutCatalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistJustCutPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "JustCutCalculatedAt",
                table: "die_cuts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JustCutEnvironment",
                table: "die_cuts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "JustCutNumberOrder",
                table: "die_cuts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "JustCutPriceAmount",
                table: "die_cuts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JustCutPriceCurrency",
                table: "die_cuts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "JustCutPriceIncludesVat",
                table: "die_cuts",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "JustCutCalculatedAt",
                table: "die_cut_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JustCutEnvironment",
                table: "die_cut_events",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "JustCutNumberOrder",
                table: "die_cut_events",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "JustCutPriceAmount",
                table: "die_cut_events",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JustCutPriceCurrency",
                table: "die_cut_events",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "JustCutPriceIncludesVat",
                table: "die_cut_events",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "JustCutCalculatedAt",
                table: "die_cuts");

            migrationBuilder.DropColumn(
                name: "JustCutEnvironment",
                table: "die_cuts");

            migrationBuilder.DropColumn(
                name: "JustCutNumberOrder",
                table: "die_cuts");

            migrationBuilder.DropColumn(
                name: "JustCutPriceAmount",
                table: "die_cuts");

            migrationBuilder.DropColumn(
                name: "JustCutPriceCurrency",
                table: "die_cuts");

            migrationBuilder.DropColumn(
                name: "JustCutPriceIncludesVat",
                table: "die_cuts");

            migrationBuilder.DropColumn(
                name: "JustCutCalculatedAt",
                table: "die_cut_events");

            migrationBuilder.DropColumn(
                name: "JustCutEnvironment",
                table: "die_cut_events");

            migrationBuilder.DropColumn(
                name: "JustCutNumberOrder",
                table: "die_cut_events");

            migrationBuilder.DropColumn(
                name: "JustCutPriceAmount",
                table: "die_cut_events");

            migrationBuilder.DropColumn(
                name: "JustCutPriceCurrency",
                table: "die_cut_events");

            migrationBuilder.DropColumn(
                name: "JustCutPriceIncludesVat",
                table: "die_cut_events");
        }
    }
}
