using DieCutCatalog.Application.Catalog;
using DieCutCatalog.Application.Employees;

namespace DieCutCatalog.Api;

internal static class ExcelImportEndpoints
{
    private const long MaximumFileSize = 20 * 1024 * 1024;

    public static IEndpointRouteBuilder MapExcelImportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/catalog-import/excel");
        group.MapPost("/preview", async (HttpContext context, IFormFile file, IExcelCatalogImportService importer,
            IAccountService accounts, CancellationToken cancellationToken) =>
        {
            var employee = await GetAdministratorAsync(context, accounts, cancellationToken);
            if (employee is null) return Results.Forbid();
            var error = ValidateFile(file);
            if (error is not null) return error;
            await using var stream = file.OpenReadStream();
            return Results.Ok(await importer.PreviewAsync(stream, cancellationToken));
        }).DisableAntiforgery();

        group.MapPost("/commit", async (HttpContext context, IFormFile file, bool overwriteExisting,
            IExcelCatalogImportService importer, IAccountService accounts, CancellationToken cancellationToken) =>
        {
            var employee = await GetAdministratorAsync(context, accounts, cancellationToken);
            if (employee is null) return Results.Forbid();
            var error = ValidateFile(file);
            if (error is not null) return error;
            await using var stream = file.OpenReadStream();
            return Results.Ok(await importer.ImportAsync(stream, employee.Id, overwriteExisting, cancellationToken));
        }).DisableAntiforgery();

        return endpoints;
    }

    private static async Task<EmployeeProfile?> GetAdministratorAsync(
        HttpContext context,
        IAccountService accounts,
        CancellationToken cancellationToken)
    {
        var header = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var token = header[prefix.Length..].Trim();
        var profile = await accounts.GetProfileAsync(token, cancellationToken);
        return profile is { MustChangePassword: false } && await accounts.IsAdministratorAsync(token, cancellationToken)
            ? profile
            : null;
    }

    private static IResult? ValidateFile(IFormFile file)
    {
        if (file.Length == 0) return Results.BadRequest(new { error = "Выбран пустой файл." });
        if (file.Length > MaximumFileSize) return Results.BadRequest(new { error = "Размер Excel-файла не должен превышать 20 МБ." });
        return !string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase)
            ? Results.BadRequest(new { error = "Поддерживаются только файлы .xlsx." })
            : null;
    }
}
