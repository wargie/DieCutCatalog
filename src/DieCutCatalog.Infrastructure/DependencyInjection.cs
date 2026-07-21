using DieCutCatalog.Application.Catalog;
using DieCutCatalog.Infrastructure.Catalog;
using DieCutCatalog.Application.Employees;
using DieCutCatalog.Domain.Employees;
using DieCutCatalog.Infrastructure.Employees;
using DieCutCatalog.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DieCutCatalog.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("ConnectionStrings:Database is required.");

        services.AddDbContext<CatalogDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.Configure<AccountOptions>(options =>
        {
            options.SetupToken = configuration["Account:SetupToken"] ?? string.Empty;
            if (int.TryParse(configuration["Account:SessionHours"], out var sessionHours))
            {
                options.SessionHours = sessionHours;
            }
        });
        services.Configure<StorageOptions>(options =>
            options.RootPath = configuration["Storage:RootPath"] ?? "storage");
        services.Configure<SmtpOptions>(options =>
        {
            options.Host = configuration["Smtp:Host"] ?? string.Empty;
            options.Username = configuration["Smtp:Username"] ?? string.Empty;
            options.Password = configuration["Smtp:Password"] ?? string.Empty;
            options.FromAddress = configuration["Smtp:FromAddress"] ?? string.Empty;
            options.FromName = configuration["Smtp:FromName"] ?? "DieCut Catalog";
            if (int.TryParse(configuration["Smtp:Port"], out var port))
            {
                options.Port = port;
            }
            if (bool.TryParse(configuration["Smtp:EnableSsl"], out var enableSsl))
            {
                options.EnableSsl = enableSsl;
            }
        });

        services.AddScoped<IPasswordHasher<Employee>, PasswordHasher<Employee>>();
        services.AddScoped<IAccountEmailSender, SmtpAccountEmailSender>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IDieCutCatalogService, DieCutCatalogService>();
        services.AddScoped<IExcelCatalogImportService, ExcelCatalogImportService>();

        return services;
    }
}
