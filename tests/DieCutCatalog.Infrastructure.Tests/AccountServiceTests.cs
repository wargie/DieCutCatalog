using System.ComponentModel.DataAnnotations;
using DieCutCatalog.Application.Catalog;
using DieCutCatalog.Application.Employees;
using DieCutCatalog.Domain.Catalog;
using DieCutCatalog.Domain.Employees;
using DieCutCatalog.Infrastructure.Catalog;
using DieCutCatalog.Infrastructure.Employees;
using DieCutCatalog.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DieCutCatalog.Infrastructure.Tests;

public sealed class AccountServiceTests
{
    [Fact]
    public async Task CreateEmployee_SendsTemporaryPassword_AndStoresOnlyHash()
    {
        await using var fixture = CreateFixture();

        var result = await fixture.Service.CreateEmployeeAsync(NewEmployee());
        var profile = result.Profile;

        var employee = await fixture.DbContext.Employees.SingleAsync();
        Assert.Equal("employee@example.com", profile.Email);
        Assert.True(profile.MustChangePassword);
        Assert.NotNull(fixture.EmailSender.TemporaryPassword);
        Assert.DoesNotContain(fixture.EmailSender.TemporaryPassword!, employee.PasswordHash);
        Assert.Equal(
            PasswordVerificationResult.Success,
            fixture.PasswordHasher.VerifyHashedPassword(
                employee,
                employee.PasswordHash,
                fixture.EmailSender.TemporaryPassword!));
    }

    [Fact]
    public async Task TemporaryPassword_MustBeChanged_BeforeNormalUse()
    {
        await using var fixture = CreateFixture();
        await fixture.Service.CreateEmployeeAsync(NewEmployee());
        var temporaryPassword = fixture.EmailSender.TemporaryPassword!;

        var firstLogin = await fixture.Service.LoginAsync(
            new LoginCommand("EMPLOYEE@example.com", temporaryPassword));

        Assert.NotNull(firstLogin);
        Assert.True(firstLogin.MustChangePassword);

        var changed = await fixture.Service.ChangePasswordAsync(
            firstLogin.AccessToken,
            new ChangePasswordCommand(temporaryPassword, "NewSecure!2026"));

        Assert.True(changed);
        Assert.Null(await fixture.Service.LoginAsync(
            new LoginCommand("employee@example.com", temporaryPassword)));

        var secondLogin = await fixture.Service.LoginAsync(
            new LoginCommand("employee@example.com", "NewSecure!2026"));

        Assert.NotNull(secondLogin);
        Assert.False(secondLogin.MustChangePassword);
    }

    [Fact]
    public async Task LoginAndLogout_AreRecordedOnceInEmployeeActivity()
    {
        await using var fixture = CreateFixture();
        var employee = await fixture.Service.CreateEmployeeAsync(NewEmployee());
        var password = fixture.EmailSender.TemporaryPassword!;

        Assert.Null(await fixture.Service.LoginAsync(new LoginCommand(employee.Profile.Email, "wrong-password")));
        var login = await fixture.Service.LoginAsync(new LoginCommand(employee.Profile.Email, password));

        Assert.NotNull(login);
        Assert.True(await fixture.Service.LogoutAsync(login.AccessToken));
        Assert.False(await fixture.Service.LogoutAsync(login.AccessToken));
        var report = await fixture.Service.GetEmployeeActivityAsync(employee.Profile.Id);

        Assert.NotNull(report);
        Assert.Collection(report.AccessActivities.OrderBy(x => x.OccurredAt),
            x => Assert.Equal(EmployeeAccessEventType.LoggedIn, x.Type),
            x => Assert.Equal(EmployeeAccessEventType.LoggedOut, x.Type));
    }
    [Fact]
    public async Task DisconnectAndResume_PreserveSessionAndRecordClientActivity()
    {
        await using var fixture = CreateFixture();
        var employee = await fixture.Service.CreateEmployeeAsync(NewEmployee());
        var password = fixture.EmailSender.TemporaryPassword!;
        var login = await fixture.Service.LoginAsync(new LoginCommand(employee.Profile.Email, password));

        Assert.NotNull(login);
        Assert.True(await fixture.Service.DisconnectSessionAsync(login.AccessToken));
        var resumed = await fixture.Service.ResumeSessionAsync(login.AccessToken);
        var profile = await fixture.Service.GetProfileAsync(login.AccessToken);
        var report = await fixture.Service.GetEmployeeActivityAsync(employee.Profile.Id);

        Assert.NotNull(resumed);
        Assert.NotNull(profile);
        Assert.NotNull(report);
        Assert.Collection(report.AccessActivities.OrderBy(x => x.OccurredAt),
            x => Assert.Equal(EmployeeAccessEventType.LoggedIn, x.Type),
            x => Assert.Equal(EmployeeAccessEventType.LoggedOut, x.Type),
            x => Assert.Equal(EmployeeAccessEventType.LoggedIn, x.Type));
    }
    [Fact]
    public async Task VerifyPassword_UsesCurrentAuthenticatedEmployee()
    {
        await using var fixture = CreateFixture();
        await fixture.Service.CreateEmployeeAsync(NewEmployee());
        var password = fixture.EmailSender.TemporaryPassword!;
        var login = await fixture.Service.LoginAsync(new LoginCommand("employee@example.com", password));

        Assert.NotNull(login);
        Assert.True(await fixture.Service.VerifyPasswordAsync(login.AccessToken, password));
        Assert.False(await fixture.Service.VerifyPasswordAsync(login.AccessToken, "wrong-password"));
    }

    [Fact]
    public async Task VerifyAdministratorPassword_RejectsEmployeeAndAcceptsAdministrator()
    {
        await using var fixture = CreateFixture();
        var employee = new Employee
        {
            Email = "operator@example.com",
            NormalizedEmail = "OPERATOR@EXAMPLE.COM",
            FirstName = "Operator",
            LastName = "User",
            Role = EmployeeRole.Employee,
            MustChangePassword = false
        };
        employee.PasswordHash = fixture.PasswordHasher.HashPassword(employee, "Employee!2026");

        var administrator = new Employee
        {
            Email = "admin@example.com",
            NormalizedEmail = "ADMIN@EXAMPLE.COM",
            FirstName = "Admin",
            LastName = "User",
            Role = EmployeeRole.Administrator,
            MustChangePassword = false
        };
        administrator.PasswordHash = fixture.PasswordHasher.HashPassword(administrator, "Admin!2026");

        fixture.DbContext.Employees.AddRange(employee, administrator);
        await fixture.DbContext.SaveChangesAsync();

        Assert.Null(await fixture.Service.VerifyAdministratorPasswordAsync("Employee!2026"));
        var verified = await fixture.Service.VerifyAdministratorPasswordAsync("Admin!2026");
        Assert.NotNull(verified);
        Assert.Equal(administrator.Id, verified.Id);
        Assert.Equal(EmployeeRole.Administrator, verified.Role);
    }
    [Fact]
    public async Task EmployeeActivity_SummarizesKnifeWork()
    {
        await using var fixture = CreateFixture();
        var employee = (await fixture.Service.CreateEmployeeAsync(NewEmployee())).Profile;
        fixture.DbContext.Equipment.Add(new Equipment
        {
            Name = "Nilpeter/Lesko",
            NormalizedName = "NILPETER/LESKO"
        });
        fixture.DbContext.CatalogReferenceEntries.AddRange(
            new CatalogReferenceEntry { Kind = CatalogReferenceKind.Material, Name = "Paper", NormalizedName = "PAPER" },
            new CatalogReferenceEntry { Kind = CatalogReferenceKind.Figure, Name = "прямоугольник", NormalizedName = "ПРЯМОУГОЛЬНИК" });
        await fixture.DbContext.SaveChangesAsync();
        var catalog = new DieCutCatalogService(fixture.DbContext);
        var knife = await catalog.CreateAsync(new SaveDieCutCommand(
            "001", null, "Nilpeter/Lesko", 96, 58, 74, 4, 4, 2, 1.5m,
            "Paper", 430, "прямоугольник", null, new DateOnly(2026, 7, 23),
            DieCutStatus.Active), employee.Id);
        await catalog.AddCirculationAsync(knife.Id, 1000, employee.Id);

        var report = await fixture.Service.GetEmployeeActivityAsync(employee.Id);

        Assert.NotNull(report);
        Assert.Equal(1, report.KnivesCount);
        Assert.Equal(1, report.CreatedCount);
        Assert.Equal(1000, report.TotalCirculation);
        Assert.Equal(2, report.Activities.Count);
    }
    [Fact]
    public async Task DeactivateEmployee_BlocksLogin_AndRejectsSelfDeletion()
    {
        await using var fixture = CreateFixture();
        var result = await fixture.Service.CreateEmployeeAsync(NewEmployee());
        var password = fixture.EmailSender.TemporaryPassword!;

        await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.Service.DeactivateEmployeeAsync(result.Profile.Id, result.Profile.Id));
        var deactivated = await fixture.Service.DeactivateEmployeeAsync(result.Profile.Id, Guid.NewGuid());

        Assert.NotNull(deactivated);
        Assert.False(deactivated.IsActive);
        Assert.Null(await fixture.Service.LoginAsync(new LoginCommand(result.Profile.Email, password)));
    }
    [Fact]
    public async Task DuplicateEmail_IsRejectedCaseInsensitively()
    {
        await using var fixture = CreateFixture();
        await fixture.Service.CreateEmployeeAsync(NewEmployee());

        await Assert.ThrowsAsync<DuplicateEmailException>(() =>
            fixture.Service.CreateEmployeeAsync(
                NewEmployee() with { Email = "EMPLOYEE@EXAMPLE.COM" }));
    }

    [Fact]
    public async Task EmailFailure_ReturnsTemporaryPassword_AndKeepsAccount()
    {
        await using var fixture = CreateFixture(emailShouldFail: true);

        var result = await fixture.Service.CreateEmployeeAsync(NewEmployee());

        Assert.False(result.EmailDelivered);
        Assert.False(string.IsNullOrWhiteSpace(result.TemporaryPassword));
        Assert.True(await fixture.DbContext.Employees.AnyAsync());
    }

    private static CreateEmployeeCommand NewEmployee() => new(
        "employee@example.com",
        "Иван",
        "Петров",
        "Оператор",
        "+7 000 000-00-00",
        EmployeeRole.Employee);

    private static TestFixture CreateFixture(bool emailShouldFail = false)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var dbContext = new CatalogDbContext(options);
        var passwordHasher = new PasswordHasher<Employee>();
        var emailSender = new CapturingEmailSender(emailShouldFail);
        var storagePath = Path.Combine(Path.GetTempPath(), "diecut-tests", Guid.NewGuid().ToString("N"));

        var service = new AccountService(
            dbContext,
            passwordHasher,
            emailSender,
            Options.Create(new AccountOptions
            {
                SessionHours = 12,
                SetupToken = "test-setup-token"
            }),
            Options.Create(new StorageOptions
            {
                RootPath = storagePath
            }));

        return new TestFixture(dbContext, passwordHasher, emailSender, service, storagePath);
    }

    private sealed class CapturingEmailSender(bool shouldFail) : IAccountEmailSender
    {
        public string? TemporaryPassword { get; private set; }

        public Task SendTemporaryPasswordAsync(
            string recipientEmail,
            string employeeName,
            string temporaryPassword,
            CancellationToken cancellationToken = default)
        {
            if (shouldFail)
            {
                throw new EmailDeliveryUnavailableException("SMTP unavailable.");
            }

            TemporaryPassword = temporaryPassword;
            return Task.CompletedTask;
        }
    }

    private sealed class TestFixture(
        CatalogDbContext dbContext,
        PasswordHasher<Employee> passwordHasher,
        CapturingEmailSender emailSender,
        AccountService service,
        string storagePath)
        : IAsyncDisposable
    {
        public CatalogDbContext DbContext { get; } = dbContext;
        public PasswordHasher<Employee> PasswordHasher { get; } = passwordHasher;
        public CapturingEmailSender EmailSender { get; } = emailSender;
        public AccountService Service { get; } = service;

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            if (Directory.Exists(storagePath))
            {
                Directory.Delete(storagePath, true);
            }
        }
    }
}
