using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DieCutCatalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReferenceValueArticles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArticleRtf",
                table: "reference_directory_values",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArticleRtf",
                table: "equipment",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArticleRtf",
                table: "catalog_reference_entries",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArticleRtf",
                table: "reference_directory_values");

            migrationBuilder.DropColumn(
                name: "ArticleRtf",
                table: "equipment");

            migrationBuilder.DropColumn(
                name: "ArticleRtf",
                table: "catalog_reference_entries");
        }
    }
}
