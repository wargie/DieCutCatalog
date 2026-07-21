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
            string? search, string? equipment, string? material, string? shape, DieCutStatus? status,
            decimal? minWidthMm, decimal? maxWidthMm, decimal? minLengthMm, decimal? maxLengthMm,
            decimal? shaftRepeatMm, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default) =>
        {
            if (await AuthorizeAsync(context, accounts, cancellationToken) is null) return Results.Unauthorized();
            return Results.Ok(await catalog.SearchAsync(new DieCutQuery(search, equipment, material, shape, status,
                minWidthMm, maxWidthMm, minLengthMm, maxLengthMm, shaftRepeatMm, page, pageSize), cancellationToken));
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

        return endpoints;
    }

    private static async Task<EmployeeProfile?> AuthorizeAsync(HttpContext context, IAccountService accounts, CancellationToken cancellationToken)
    {
        var header = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        return await accounts.GetProfileAsync(header[prefix.Length..].Trim(), cancellationToken);
    }

    private static IResult PasswordChangeRequired() => Results.Json(new { error = "Необходимо заменить временный пароль." }, statusCode: StatusCodes.Status428PreconditionRequired);
}

internal sealed record SaveDieCutRequest(
    string Number, string Equipment, decimal ShaftRepeatMm, decimal WidthMm, decimal LengthMm,
    int Streams, int Repeats, decimal GapAcrossMm, decimal GapAlongMm, string Material,
    decimal MaterialWidthMm, decimal? KnifeHeightMicrons, string Shape, string? Comments, DateOnly? CommissionedOn, DieCutStatus Status)
{
    public SaveDieCutCommand ToCommand() => new(Number, Equipment, ShaftRepeatMm, WidthMm, LengthMm, Streams,
        Repeats, GapAcrossMm, GapAlongMm, Material, MaterialWidthMm, KnifeHeightMicrons, Shape, Comments, CommissionedOn, Status);
}
