using DieCutCatalog.Application.Employees;
using DieCutCatalog.Domain.Employees;
using DieCutCatalog.Infrastructure.Employees;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;

namespace DieCutCatalog.PostgreSql.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class EmployeeRoleUpgradePostgreSqlTests(PostgreSqlFixture fixture)
{
    private const string LastVersion190Migration = "20260817100735_AddReferenceValueArticles";

    [Fact]
    public async Task Upgrade_from_190_migrates_legacy_employee_role_and_preserves_login()
    {
        var connectionString = await fixture.CreateIsolatedDatabaseConnectionStringAsync();
        var employeeId = Guid.NewGuid();
        const string email = "legacy-operator@example.test";
        const string password = "LegacyOperator!2026";
        var now = DateTimeOffset.UtcNow;
        var passwordHasher = new PasswordHasher<Employee>();
        var legacyEmployee = new Employee { Id = employeeId };
        var passwordHash = passwordHasher.HashPassword(legacyEmployee, password);

        await using (var legacyContext = fixture.CreateDbContext(connectionString))
        {
            await legacyContext.GetService<IMigrator>().MigrateAsync(LastVersion190Migration);
            await legacyContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO employees
                    ("Id", "Email", "NormalizedEmail", "PasswordHash", "MustChangePassword",
                     "Role", "IsActive", "FirstName", "LastName", "CreatedAt", "UpdatedAt")
                VALUES
                    ({employeeId}, {email}, {email.ToUpperInvariant()}, {passwordHash}, {false},
                     {"Employee"}, {true}, {"Legacy"}, {"Operator"}, {now}, {now});
                """);
            var storedLegacyRole = await legacyContext.Database.SqlQuery<string>(
                    $"""SELECT "Role" AS "Value" FROM employees WHERE "Id" = {employeeId}""")
                .SingleAsync();
            Assert.Equal("Employee", storedLegacyRole);

            await legacyContext.Database.MigrateAsync();
        }

        await using var currentContext = fixture.CreateDbContext(connectionString);
        var storedEmployee = await currentContext.Employees.AsNoTracking()
            .SingleAsync(x => x.Id == employeeId);
        Assert.Equal(EmployeeRole.Operator, storedEmployee.Role);

        var accountService = new AccountService(
            currentContext,
            passwordHasher,
            new NoOpEmailSender(),
            Options.Create(new AccountOptions { SessionHours = 12 }),
            Options.Create(new StorageOptions { RootPath = Path.GetTempPath() }));

        var login = await accountService.LoginAsync(new LoginCommand(email, password));

        Assert.NotNull(login);
        Assert.Equal(employeeId, login.Profile.Id);
        Assert.Equal(EmployeeRole.Operator, login.Profile.Role);
    }

    private sealed class NoOpEmailSender : IAccountEmailSender
    {
        public Task SendTemporaryPasswordAsync(
            string recipientEmail,
            string employeeName,
            string temporaryPassword,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
