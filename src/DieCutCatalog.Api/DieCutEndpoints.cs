using DieCutCatalog.Application.Catalog;
using DieCutCatalog.Application.Employees;
using DieCutCatalog.Domain.Catalog;

namespace DieCutCatalog.Api;

internal static class DieCutEndpoints
{
    public static IEndpointRouteBuilder MapDieCutEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/die-cuts");

        group.MapGet("/", async (HttpContext context, IDieCutCatalogService catalog, IAccountService accounts,
            string? search, string? equipment, string? material, string? figure, DieCutStatus? status,
            decimal? minX, decimal? maxX, decimal? minY, decimal? maxY,
            int? shaft, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default) =>
        {
            if (await AuthorizeAsync(context, accounts, cancellationToken) is null) return Results.Unauthorized();
            return Results.Ok(await catalog.SearchAsync(new DieCutQuery(search, equipment, material, figure, status,
                minX, maxX, minY, maxY, shaft, page, pageSize), cancellationToken));
        });

        group.MapGet("/facets", async (HttpContext context, IDieCutCatalogService catalog, IAccountService accounts, CancellationToken cancellationToken) =>
            await AuthorizeAsync(context, accounts, cancellationToken) is null
                ? Results.Unauthorized()
                : Results.Ok(await catalog.GetFacetsAsync(cancellationToken)));

        group.MapGet("/{id:guid}", async (Guid id, HttpContext context, IDieCutCatalogService catalog, IAccountService accounts, CancellationToken cancellationToken) =>
        {
            if (await AuthorizeAsync(context, accounts, cancellationToken) is null) return Results.Unauthorized();
            var dieCut = await catalog.GetAsync(id, cancellationToken);
            return dieCut is null ? Results.NotFound() : Results.Ok(dieCut);
        });

        group.MapGet("/{id:guid}/events", async (Guid id, HttpContext context, IDieCutCatalogService catalog, IAccountService accounts, CancellationToken cancellationToken) =>
        {
            if (await AuthorizeAsync(context, accounts, cancellationToken) is null) return Results.Unauthorized();
            var events = await catalog.GetEventsAsync(id, cancellationToken);
            return events is null ? Results.NotFound() : Results.Ok(events);
        });

        group.MapPost("/", async (HttpContext context, SaveDieCutRequest request, IDieCutCatalogService catalog, IAccountService accounts, CancellationToken cancellationToken) =>
        {
            var employee = await AuthorizeAsync(context, accounts, cancellationToken);
            if (employee is null) return Results.Unauthorized();
            if (employee.MustChangePassword) return PasswordChangeRequired();
            var dieCut = await catalog.CreateAsync(request.ToCommand(), employee.Id, cancellationToken);
            return Results.Created($"/api/die-cuts/{dieCut.Id}", dieCut);
        });

        group.MapPut("/{id:guid}", async (Guid id, HttpContext context, SaveDieCutRequest request, IDieCutCatalogService catalog, IAccountService accounts, CancellationToken cancellationToken) =>
        {
            var employee = await AuthorizeAsync(context, accounts, cancellationToken);
            if (employee is null) return Results.Unauthorized();
            if (employee.MustChangePassword) return PasswordChangeRequired();
            var dieCut = await catalog.UpdateAsync(id, request.ToCommand(), employee.Id, cancellationToken);
            return dieCut is null ? Results.NotFound() : Results.Ok(dieCut);
        });

        group.MapPost("/{id:guid}/circulations", async (
            Guid id,
            HttpContext context,
            AddCirculationRequest request,
            IDieCutCatalogService catalog,
            IAccountService accounts,
            CancellationToken cancellationToken) =>
        {
            var employee = await AuthorizeAsync(context, accounts, cancellationToken);
            if (employee is null) return Results.Unauthorized();
            if (employee.MustChangePassword) return PasswordChangeRequired();
            var dieCut = await catalog.AddCirculationAsync(id, request.Quantity, employee.Id, cancellationToken);
            return dieCut is null ? Results.NotFound() : Results.Ok(dieCut);
        });

        group.MapPost("/{id:guid}/reset-mileage", async (
            Guid id,
            HttpContext context,
            PasswordConfirmationRequest request,
            IDieCutCatalogService catalog,
            IAccountService accounts,
            CancellationToken cancellationToken) =>
        {
            var employee = await AuthorizeAsync(context, accounts, cancellationToken);
            if (employee is null) return Results.Unauthorized();
            if (employee.MustChangePassword) return PasswordChangeRequired();
            var token = GetBearerToken(context);
            if (token is null || !await accounts.VerifyPasswordAsync(token, request.Password, cancellationToken))
                return InvalidPassword();

            var dieCut = await catalog.ResetMileageAsync(id, employee.Id, cancellationToken);
            return dieCut is null ? Results.NotFound() : Results.Ok(dieCut);
        }).RequireRateLimiting("auth");

        group.MapPost("/{id:guid}/retire", async (
            Guid id,
            HttpContext context,
            PasswordConfirmationRequest request,
            IDieCutCatalogService catalog,
            IAccountService accounts,
            CancellationToken cancellationToken) =>
        {
            var employee = await AuthorizeAsync(context, accounts, cancellationToken);
            if (employee is null) return Results.Unauthorized();
            if (employee.MustChangePassword) return PasswordChangeRequired();
            var token = GetBearerToken(context);
            if (token is null || !await accounts.VerifyPasswordAsync(token, request.Password, cancellationToken))
                return InvalidPassword();

            var dieCut = await catalog.RetireAsync(id, employee.Id, cancellationToken);
            return dieCut is null ? Results.NotFound() : Results.Ok(dieCut);
        }).RequireRateLimiting("auth");

        return endpoints;
    }

    private static async Task<EmployeeProfile?> AuthorizeAsync(HttpContext context, IAccountService accounts, CancellationToken cancellationToken)
    {
        var token = GetBearerToken(context);
        return token is null ? null : await accounts.GetProfileAsync(token, cancellationToken);
    }

    private static string? GetBearerToken(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header[prefix.Length..].Trim()
            : null;
    }

    private static IResult PasswordChangeRequired() =>
        Results.Json(new { error = "Необходимо заменить временный пароль." }, statusCode: StatusCodes.Status428PreconditionRequired);

    private static IResult InvalidPassword() =>
        Results.Json(new { error = "Неверный пароль. Операция отменена." }, statusCode: StatusCodes.Status401Unauthorized);
}

internal sealed record SaveDieCutRequest(
    string Number, string? JcOrderNumber, string Equipment, int Shaft, decimal X, decimal Y,
    int Streams, int Repeats, decimal GrooveSpacing, decimal LabelCornerRadius, string Material, decimal H, string Figure, string? Comments, DateOnly? Date, DieCutStatus Status)
{
    public SaveDieCutCommand ToCommand() => new(Number, JcOrderNumber, Equipment, Shaft, X, Y, Streams,
        Repeats, GrooveSpacing, LabelCornerRadius, Material, H, Figure, Comments, Date, Status);
}

internal sealed record AddCirculationRequest(long Quantity);
internal sealed record PasswordConfirmationRequest(string Password);