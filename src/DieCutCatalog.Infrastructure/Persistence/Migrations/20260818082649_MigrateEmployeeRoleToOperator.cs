using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DieCutCatalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MigrateEmployeeRoleToOperator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE employees
                SET "Role" = 'Operator'
                WHERE "Role" = 'Employee';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE employees
                SET "Role" = 'Employee'
                WHERE "Role" = 'Operator';
                """);
        }
    }
}
