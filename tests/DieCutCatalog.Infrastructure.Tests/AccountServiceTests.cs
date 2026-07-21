using DieCutCatalog.Application.Employees;
using DieCutCatalog.Domain.Employees;
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

        var profile = await fixture.Service.CreateEmployeeAsync(NewEmployee());

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
    public async Task DuplicateEmail_IsRejectedCaseInsensitively()
    {
        await using var fixture = CreateFixture();
        await fixture.Service.CreateEmployeeAsync(NewEmployee());

        await Assert.ThrowsAsync<DuplicateEmailException>(() =>
            fixture.Service.CreateEmployeeAsync(
                NewEmployee() with { Email = "EMPLOYEE@EXAMPLE.COM" }));
    }

    [Fact]
    public async Task EmailFailure_RollsBackCreatedAccount()
    {
        await using var fixture = CreateFixture(emailShouldFail: true);

        await Assert.ThrowsAsync<EmailDeliveryUnavailableException>(() =>
            fixture.Service.CreateEmployeeAsync(NewEmployee()));

        Assert.False(await fixture.DbContext.Employees.AnyAsync());
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
