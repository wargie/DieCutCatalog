using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DieCutCatalog.Infrastructure.Persistence.Migrations;

public partial class SeedDefaultCatalogFigures : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO catalog_reference_entries ("Id", "Kind", "Name", "NormalizedName", "UpdatedAt") VALUES
                ('80000000-0000-0000-0000-000000000001', 'Figure', 'прямоугольник', 'ПРЯМОУГОЛЬНИК', NOW()),
                ('80000000-0000-0000-0000-000000000002', 'Figure', 'круг', 'КРУГ', NOW()),
                ('80000000-0000-0000-0000-000000000003', 'Figure', 'квадрат', 'КВАДРАТ', NOW()),
                ('80000000-0000-0000-0000-000000000004', 'Figure', 'специальная форма', 'СПЕЦИАЛЬНАЯ ФОРМА', NOW()),
                ('80000000-0000-0000-0000-000000000005', 'Figure', 'перфорация', 'ПЕРФОРАЦИЯ', NOW())
            ON CONFLICT ("Kind", "NormalizedName") DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM catalog_reference_entries
            WHERE "Id" IN (
                '80000000-0000-0000-0000-000000000001',
                '80000000-0000-0000-0000-000000000002',
                '80000000-0000-0000-0000-000000000003',
                '80000000-0000-0000-0000-000000000004',
                '80000000-0000-0000-0000-000000000005');
            """);
    }
}