using DieCutCatalog.Api;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using DieCutCatalog.Application.Catalog;
using DieCutCatalog.Application.Employees;
using DieCutCatalog.Domain.Employees;
using DieCutCatalog.Infrastructure;
using DieCutCatalog.Infrastructure.Employees;
using DieCutCatalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("auth", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
});

var app = builder.Build();

app.UseRateLimiter();

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (ValidationException exception)
    {
        await WriteErrorAsync(context, StatusCodes.Status400BadRequest, exception.Message);
    }
    catch (InvalidDataException exception)
    {
        await WriteErrorAsync(context, StatusCodes.Status400BadRequest, exception.Message);
    }
    catch (DuplicateEmailException exception)
    {
        await WriteErrorAsync(context, StatusCodes.Status409Conflict, exception.Message);
    }
    catch (InvalidSetupTokenException exception)
    {
        await WriteErrorAsync(context, StatusCodes.Status403Forbidden, exception.Message);
    }
    catch (SetupAlreadyCompletedException exception)
    {
        await WriteErrorAsync(context, StatusCodes.Status409Conflict, exception.Message);
    }
    catch (EmailDeliveryUnavailableException)
    {
        await WriteErrorAsync(
            context,
            StatusCodes.Status503ServiceUnavailable,
            "Не удалось отправить письмо. Учётная запись не создана.");
    }
    catch (JustCutIntegrationException exception)
    {
        await WriteErrorAsync(context, StatusCodes.Status502BadGateway, exception.Message);
    }
});

app.MapGet("/health", async (
    CatalogDbContext dbContext,
    CancellationToken cancellationToken) =>
    await dbContext.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok("Healthy")
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

app.MapPost("/api/setup/administrator", async (
    HttpContext context,
    SetupAdministratorRequest request,
    IAccountService accounts,
    CancellationToken cancellationToken) =>
{
    var setupToken = context.Request.Headers["X-Setup-Token"].ToString();
    var profile = await accounts.CreateInitialAdministratorAsync(
        new CreateEmployeeCommand(
            request.Email,
            request.FirstName,
            request.LastName,
            request.Position,
            request.Phone,
            EmployeeRole.Administrator),
        setupToken,
        cancellationToken);

    return Results.Created($"/api/employees/{profile.Id}", profile);
})
.RequireRateLimiting("auth");

app.MapPost("/api/auth/login", async (
    LoginRequest request,
    IAccountService accounts,
    CancellationToken cancellationToken) =>
{
    var result = await accounts.LoginAsync(
        new LoginCommand(request.Email, request.Password),
        cancellationToken);

    return result is null ? Results.Unauthorized() : Results.Ok(result);
})
.RequireRateLimiting("auth");

app.MapPost("/api/auth/logout", async (
    HttpContext context,
    IAccountService accounts,
    CancellationToken cancellationToken) =>
{
    var token = GetBearerToken(context);
    if (token is null)
    {
        return Results.Unauthorized();
    }

    await accounts.LogoutAsync(token, cancellationToken);
    return Results.NoContent();
});
app.MapPost("/api/auth/resume", async (
    HttpContext context,
    IAccountService accounts,
    CancellationToken cancellationToken) =>
{
    var token = GetBearerToken(context);
    if (token is null) return Results.Unauthorized();

    var profile = await accounts.ResumeSessionAsync(token, cancellationToken);
    return profile is null ? Results.Unauthorized() : Results.Ok(profile);
});

app.MapPost("/api/auth/disconnect", async (
    HttpContext context,
    IAccountService accounts,
    CancellationToken cancellationToken) =>
{
    var token = GetBearerToken(context);
    if (token is null) return Results.Unauthorized();

    return await accounts.DisconnectSessionAsync(token, cancellationToken)
        ? Results.NoContent()
        : Results.Unauthorized();
});
app.MapGet("/api/employees/me", async (
    HttpContext context,
    IAccountService accounts,
    CancellationToken cancellationToken) =>
{
    var token = GetBearerToken(context);
    if (token is null)
    {
        return Results.Unauthorized();
    }

    var profile = await accounts.GetProfileAsync(token, cancellationToken);
    return profile is null ? Results.Unauthorized() : Results.Ok(profile);
});

app.MapPost("/api/employees/directory", async (
    HttpContext context,
    PasswordConfirmationRequest request,
    IAccountService accounts,
    CancellationToken cancellationToken) =>
{
    var authorization = await GetReadyProfileAsync(
        accounts, GetBearerToken(context), cancellationToken);
    if (authorization.Error is not null) return authorization.Error;
    var administrator = await accounts.VerifyAdministratorPasswordAsync(request.Password, cancellationToken);
    if (administrator is null) return AdministratorPasswordRequired();
    return Results.Ok(await accounts.GetEmployeeDirectoryAsync(cancellationToken));
})
.RequireRateLimiting("auth");
app.MapPost("/api/employees", async (
    HttpContext context,
    CreateEmployeeRequest request,
    IAccountService accounts,
    CancellationToken cancellationToken) =>
{
    var token = GetBearerToken(context);
    if (token is null || !await accounts.IsAdministratorAsync(token, cancellationToken))
    {
        return Results.Forbid();
    }

    var result = await accounts.CreateEmployeeAsync(
        new CreateEmployeeCommand(
            request.Email,
            request.FirstName,
            request.LastName,
            request.Position,
            request.Phone,
            request.IsAdministrator ? EmployeeRole.Administrator : EmployeeRole.Operator),
        cancellationToken);

    return Results.Created($"/api/employees/{result.Profile.Id}", result);
})
.RequireRateLimiting("auth");

app.MapDelete("/api/employees/{employeeId:guid}", async (
    Guid employeeId,
    HttpContext context,
    [FromBody] PasswordConfirmationRequest request,
    IAccountService accounts,
    CancellationToken cancellationToken) =>
{
    var authorization = await GetReadyProfileAsync(
        accounts, GetBearerToken(context), cancellationToken);
    if (authorization.Error is not null) return authorization.Error;
    var administrator = await accounts.VerifyAdministratorPasswordAsync(request.Password, cancellationToken);
    if (administrator is null) return AdministratorPasswordRequired();
    var employee = await accounts.DeactivateEmployeeAsync(
        employeeId, authorization.Profile!.Id, cancellationToken);
    return employee is null ? Results.NotFound() : Results.Ok(employee);
})
.RequireRateLimiting("auth");
app.MapPut("/api/employees/me", async (
    HttpContext context,
    UpdateEmployeeRequest request,
    IAccountService accounts,
    CancellationToken cancellationToken) =>
{
    var token = GetBearerToken(context);
    var authorization = await GetReadyProfileAsync(accounts, token, cancellationToken);
    if (authorization.Error is not null)
    {
        return authorization.Error;
    }

    var profile = await accounts.UpdateProfileAsync(
        token!,
        new UpdateEmployeeProfileCommand(
            request.FirstName,
            request.LastName,
            request.Position,
            request.Phone,
            request.AdditionalContacts),
        cancellationToken);

    return profile is null ? Results.Unauthorized() : Results.Ok(profile);
});

app.MapPost("/api/employees/me/change-password", async (
    HttpContext context,
    ChangePasswordRequest request,
    IAccountService accounts,
    CancellationToken cancellationToken) =>
{
    var token = GetBearerToken(context);
    if (token is null)
    {
        return Results.Unauthorized();
    }

    var changed = await accounts.ChangePasswordAsync(
        token,
        new ChangePasswordCommand(request.CurrentPassword, request.NewPassword),
        cancellationToken);

    return changed ? Results.NoContent() : Results.BadRequest(new
    {
        error = "Текущий пароль указан неверно."
    });
});

app.MapPost("/api/employees/me/change-email", async (
    HttpContext context,
    ChangeEmailRequest request,
    IAccountService accounts,
    CancellationToken cancellationToken) =>
{
    var token = GetBearerToken(context);
    var authorization = await GetReadyProfileAsync(accounts, token, cancellationToken);
    if (authorization.Error is not null)
    {
        return authorization.Error;
    }

    var changed = await accounts.ChangeEmailAsync(
        token!,
        new ChangeEmailCommand(request.CurrentPassword, request.NewEmail),
        cancellationToken);

    return changed ? Results.NoContent() : Results.BadRequest(new
    {
        error = "Текущий пароль указан неверно."
    });
});

app.MapPost("/api/employees/me/photo", async (
    HttpContext context,
    IFormFile photo,
    IAccountService accounts,
    CancellationToken cancellationToken) =>
{
    var token = GetBearerToken(context);
    var authorization = await GetReadyProfileAsync(accounts, token, cancellationToken);
    if (authorization.Error is not null)
    {
        return authorization.Error;
    }

    await using var stream = photo.OpenReadStream();
    var profile = await accounts.SavePhotoAsync(
        token!,
        new StoredPhoto(photo.FileName, photo.ContentType, stream, photo.Length),
        cancellationToken);

    return profile is null ? Results.Unauthorized() : Results.Ok(profile);
})
.DisableAntiforgery();

app.MapGet("/api/employees/{employeeId:guid}/photo", async (
    Guid employeeId,
    HttpContext context,
    IAccountService accounts,
    CatalogDbContext dbContext,
    IOptions<StorageOptions> storageOptions,
    CancellationToken cancellationToken) =>
{
    var token = GetBearerToken(context);
    if (token is null || await accounts.GetProfileAsync(token, cancellationToken) is null)
    {
        return Results.Unauthorized();
    }

    var photoFileName = await dbContext.Employees
        .Where(x => x.Id == employeeId && x.IsActive)
        .Select(x => x.PhotoFileName)
        .SingleOrDefaultAsync(cancellationToken);

    if (string.IsNullOrWhiteSpace(photoFileName))
    {
        return Results.NotFound();
    }

    var storageRoot = Path.GetFullPath(storageOptions.Value.RootPath);
    var fullPath = Path.GetFullPath(Path.Combine(storageRoot, photoFileName));
    if (!fullPath.StartsWith(storageRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        || !File.Exists(fullPath))
    {
        return Results.NotFound();
    }

    var contentType = Path.GetExtension(fullPath).ToLowerInvariant() switch
    {
        ".jpg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "application/octet-stream"
    };

    return Results.File(fullPath, contentType);
});

app.MapDieCutEndpoints();
app.MapCatalogAdministrationEndpoints();
app.MapExcelImportEndpoints();
app.MapUpdateEndpoints();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.Run();

static IResult AdministratorPasswordRequired() => Results.Json(
    new { error = "Недостаточно прав: требуется пароль администратора." },
    statusCode: StatusCodes.Status403Forbidden);
static string? GetBearerToken(HttpContext context)
{
    var header = context.Request.Headers.Authorization.ToString();
    const string prefix = "Bearer ";
    return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        ? header[prefix.Length..].Trim()
        : null;
}

static async Task<(EmployeeProfile? Profile, IResult? Error)> GetReadyProfileAsync(
    IAccountService accounts,
    string? token,
    CancellationToken cancellationToken)
{
    if (token is null)
    {
        return (null, Results.Unauthorized());
    }

    var profile = await accounts.GetProfileAsync(token, cancellationToken);
    if (profile is null)
    {
        return (null, Results.Unauthorized());
    }

    if (profile.MustChangePassword)
    {
        return (profile, Results.Json(
            new { error = "Необходимо заменить временный пароль." },
            statusCode: StatusCodes.Status428PreconditionRequired));
    }

    return (profile, null);
}

static async Task WriteErrorAsync(HttpContext context, int statusCode, string message)
{
    if (context.Response.HasStarted)
    {
        return;
    }

    context.Response.StatusCode = statusCode;
    await context.Response.WriteAsJsonAsync(new { error = message });
}

internal sealed record SetupAdministratorRequest(
    string Email,
    string FirstName,
    string LastName,
    string? Position,
    string? Phone);

internal sealed record CreateEmployeeRequest(
    string Email,
    string FirstName,
    string LastName,
    string? Position,
    string? Phone,
    bool IsAdministrator = false);

internal sealed record LoginRequest(string Email, string Password);

internal sealed record UpdateEmployeeRequest(
    string FirstName,
    string LastName,
    string? Position,
    string? Phone,
    string? AdditionalContacts);

internal sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

internal sealed record ChangeEmailRequest(string CurrentPassword, string NewEmail);
