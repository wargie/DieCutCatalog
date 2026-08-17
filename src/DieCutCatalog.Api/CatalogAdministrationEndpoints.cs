using DieCutCatalog.Application.Catalog;
using DieCutCatalog.Application.Employees;
using DieCutCatalog.Domain.Employees;
using Microsoft.AspNetCore.Mvc;

namespace DieCutCatalog.Api;

internal static class CatalogAdministrationEndpoints
{
    public static IEndpointRouteBuilder MapCatalogAdministrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/catalog-administration");

        group.MapGet("/references", async (HttpContext context, ICatalogAdministrationService service,
            IAccountService accounts, CancellationToken cancellationToken) =>
        {
            var profile = await AuthorizeAsync(context, accounts, cancellationToken);
            return profile is null ? Results.Unauthorized() : Results.Ok(await service.GetReferencesAsync(cancellationToken));
        });

        group.MapPost("/references/{type}", async (CatalogReferenceType type, ReferenceNameRequest request,
            HttpContext context, ICatalogAdministrationService service, IAccountService accounts,
            CancellationToken cancellationToken) =>
        {
            var profile = await AuthorizeAsync(context, accounts, cancellationToken);
            if (profile is null) return Results.Unauthorized();
            if (profile.Role != EmployeeRole.Administrator) return AdministratorRequired();
            return Results.Ok(await service.AddReferenceAsync(type, request.Name, cancellationToken));
        });

        group.MapPost("/references/{type}/import", async (CatalogReferenceType type, ReferenceImportCommand request,
            HttpContext context, ICatalogAdministrationService service, IAccountService accounts,
            CancellationToken cancellationToken) =>
        {
            var profile = await AuthorizeAsync(context, accounts, cancellationToken);
            if (profile is null) return Results.Unauthorized();
            if (profile.Role != EmployeeRole.Administrator) return AdministratorRequired();
            return Results.Ok(await service.ImportReferencesAsync(type, request.Names, cancellationToken));
        });

        group.MapPut("/references/{type}/{id:guid}", async (CatalogReferenceType type, Guid id,
            ReferenceNameRequest request, HttpContext context, ICatalogAdministrationService service,
            IAccountService accounts, CancellationToken cancellationToken) =>
        {
            var profile = await AuthorizeAsync(context, accounts, cancellationToken);
            if (profile is null) return Results.Unauthorized();
            if (profile.Role != EmployeeRole.Administrator) return AdministratorRequired();
            var item = await service.RenameReferenceAsync(type, id, request.Name, cancellationToken);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        group.MapDelete("/references/{type}/{id:guid}", async (CatalogReferenceType type, Guid id,
            [FromBody] PasswordConfirmationRequest request, HttpContext context, ICatalogAdministrationService service,
            IAccountService accounts, CancellationToken cancellationToken) =>
        {
            var profile = await AuthorizeAsync(context, accounts, cancellationToken);
            if (profile is null) return Results.Unauthorized();
            if (profile.MustChangePassword) return PasswordChangeRequired();
            var administrator = await accounts.VerifyAdministratorPasswordAsync(request.Password, cancellationToken);
            if (administrator is null) return AdministratorPasswordRequired();
            return await service.DeleteReferenceAsync(type, id, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound();
        }).RequireRateLimiting("auth");

        group.MapGet("/directories", async (HttpContext context, ICatalogAdministrationService service,
            IAccountService accounts, CancellationToken cancellationToken) =>
        {
            var profile = await AuthorizeAsync(context, accounts, cancellationToken);
            return profile is null ? Results.Unauthorized() : Results.Ok(await service.GetDirectoryOverviewAsync(cancellationToken));
        });

        group.MapPost("/directory-groups", async (ReferenceNameRequest request, HttpContext context,
            ICatalogAdministrationService service, IAccountService accounts, CancellationToken cancellationToken) =>
        {
            var profile = await AuthorizeAsync(context, accounts, cancellationToken);
            if (profile is null) return Results.Unauthorized();
            if (profile.Role != EmployeeRole.Administrator) return AdministratorRequired();
            return Results.Ok(await service.AddDirectoryGroupAsync(request.Name, cancellationToken));
        });

        group.MapDelete("/directory-groups/{id:guid}", async (Guid id,
            [FromBody] PasswordConfirmationRequest request, HttpContext context,
            ICatalogAdministrationService service, IAccountService accounts,
            CancellationToken cancellationToken) =>
        {
            var profile = await AuthorizeAsync(context, accounts, cancellationToken);
            if (profile is null) return Results.Unauthorized();
            if (profile.MustChangePassword) return PasswordChangeRequired();
            var administrator = await accounts.VerifyAdministratorPasswordAsync(request.Password, cancellationToken);
            if (administrator is null) return AdministratorPasswordRequired();
            return await service.DeleteDirectoryGroupAsync(id, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound();
        }).RequireRateLimiting("auth");

        group.MapPost("/directories", async (CreateReferenceDirectoryCommand request, HttpContext context,
            ICatalogAdministrationService service, IAccountService accounts, CancellationToken cancellationToken) =>
        {
            var profile = await AuthorizeAsync(context, accounts, cancellationToken);
            if (profile is null) return Results.Unauthorized();
            if (profile.Role != EmployeeRole.Administrator) return AdministratorRequired();
            return Results.Ok(await service.AddDirectoryAsync(request, cancellationToken));
        });

        group.MapPut("/directories/{id:guid}", async (Guid id, UpdateReferenceDirectoryCommand request,
            HttpContext context, ICatalogAdministrationService service, IAccountService accounts,
            CancellationToken cancellationToken) =>
        {
            var profile = await AuthorizeAsync(context, accounts, cancellationToken);
            if (profile is null) return Results.Unauthorized();
            if (profile.Role != EmployeeRole.Administrator) return AdministratorRequired();
            var item = await service.UpdateDirectoryAsync(id, request, cancellationToken);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        group.MapPut("/references/{type}/{id:guid}/article", async (CatalogReferenceType type, Guid id,
            ReferenceArticleCommand request, HttpContext context, ICatalogAdministrationService service,
            IAccountService accounts, CancellationToken cancellationToken) =>
        {
            var profile = await AuthorizeAsync(context, accounts, cancellationToken);
            if (profile is null) return Results.Unauthorized();
            if (profile.Role != EmployeeRole.Administrator) return AdministratorRequired();
            return await service.UpdateReferenceArticleAsync(type, id, request.ArticleRtf, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound();
        });

        group.MapDelete("/directories/{id:guid}", async (Guid id, HttpContext context,
            ICatalogAdministrationService service, IAccountService accounts, CancellationToken cancellationToken) =>
        {
            var profile = await AuthorizeAsync(context, accounts, cancellationToken);
            if (profile is null) return Results.Unauthorized();
            if (profile.Role != EmployeeRole.Administrator) return AdministratorRequired();
            return await service.DeleteDirectoryAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();
        });

        group.MapGet("/directories/{id:guid}/values", async (Guid id, bool includeArchived, HttpContext context,
            ICatalogAdministrationService service, IAccountService accounts, CancellationToken cancellationToken) =>
        {
            var profile = await AuthorizeAsync(context, accounts, cancellationToken);
            return profile is null ? Results.Unauthorized() : Results.Ok(await service.GetDirectoryValuesAsync(id, includeArchived, cancellationToken));
        });

        group.MapPost("/directories/{id:guid}/values", async (Guid id, ReferenceNameRequest request,
            HttpContext context, ICatalogAdministrationService service, IAccountService accounts,
            CancellationToken cancellationToken) =>
        {
            var profile = await AuthorizeAsync(context, accounts, cancellationToken);
            if (profile is null) return Results.Unauthorized();
            if (profile.Role != EmployeeRole.Administrator) return AdministratorRequired();
            return Results.Ok(await service.AddDirectoryValueAsync(id, request.Name, cancellationToken));
        });

        group.MapPost("/directories/{id:guid}/values/import", async (Guid id, ReferenceImportCommand request,
            HttpContext context, ICatalogAdministrationService service, IAccountService accounts,
            CancellationToken cancellationToken) =>
        {
            var profile = await AuthorizeAsync(context, accounts, cancellationToken);
            if (profile is null) return Results.Unauthorized();
            if (profile.Role != EmployeeRole.Administrator) return AdministratorRequired();
            return Results.Ok(await service.ImportDirectoryValuesAsync(id, request.Names, cancellationToken));
        });

        group.MapPut("/directories/{directoryId:guid}/values/{id:guid}", async (Guid directoryId, Guid id,
            ReferenceValueRequest request, HttpContext context, ICatalogAdministrationService service,
            IAccountService accounts, CancellationToken cancellationToken) =>
        {
            var profile = await AuthorizeAsync(context, accounts, cancellationToken);
            if (profile is null) return Results.Unauthorized();
            if (profile.Role != EmployeeRole.Administrator) return AdministratorRequired();
            var item = await service.UpdateDirectoryValueAsync(directoryId, id, request.Name, request.IsArchived, cancellationToken);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        group.MapDelete("/directories/{directoryId:guid}/values/{id:guid}", async (Guid directoryId, Guid id,
            HttpContext context, ICatalogAdministrationService service, IAccountService accounts,
            CancellationToken cancellationToken) =>
        {
            var profile = await AuthorizeAsync(context, accounts, cancellationToken);
            if (profile is null) return Results.Unauthorized();
            if (profile.Role != EmployeeRole.Administrator) return AdministratorRequired();
            return await service.DeleteDirectoryValueAsync(directoryId, id, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound();
        });

        group.MapPut("/directories/{directoryId:guid}/values/{id:guid}/article", async (
            Guid directoryId, Guid id, ReferenceArticleCommand request, HttpContext context,
            ICatalogAdministrationService service, IAccountService accounts, CancellationToken cancellationToken) =>
        {
            var profile = await AuthorizeAsync(context, accounts, cancellationToken);
            if (profile is null) return Results.Unauthorized();
            if (profile.Role != EmployeeRole.Administrator) return AdministratorRequired();
            return await service.UpdateDirectoryValueArticleAsync(
                    directoryId, id, request.ArticleRtf, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound();
        });
        group.MapGet("/audit-log", async (string? search, int page, int pageSize, HttpContext context,
            ICatalogAdministrationService service, IAccountService accounts, CancellationToken cancellationToken) =>
        {
            var profile = await AuthorizeAsync(context, accounts, cancellationToken);
            return profile is null
                ? Results.Unauthorized()
                : Results.Ok(await service.SearchAuditLogAsync(search, page, pageSize, cancellationToken));
        });

        group.MapGet("/audit-log/export", async (string? search, string format, HttpContext context,
            ICatalogAdministrationService service, IAccountService accounts, CancellationToken cancellationToken) =>
        {
            var profile = await AuthorizeAsync(context, accounts, cancellationToken);
            if (profile is null) return Results.Unauthorized();
            var file = await service.ExportAuditLogAsync(search, format.Equals("pdf", StringComparison.OrdinalIgnoreCase), cancellationToken);
            return Results.File(file.Content, file.ContentType, file.FileName);
        });

        return endpoints;
    }

    private static async Task<EmployeeProfile?> AuthorizeAsync(
        HttpContext context, IAccountService accounts, CancellationToken cancellationToken)
    {
        var header = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        return await accounts.GetProfileAsync(header[prefix.Length..].Trim(), cancellationToken);
    }

    private static IResult AdministratorPasswordRequired() => Results.Json(
        new { error = "Недостаточно прав: требуется пароль суперпользователя." },
        statusCode: StatusCodes.Status403Forbidden);

    private static IResult PasswordChangeRequired() => Results.Json(
        new { error = "Сначала измените временный пароль." },
        statusCode: StatusCodes.Status403Forbidden);
    private static IResult AdministratorRequired() => Results.Json(
        new { error = "Недостаточно прав: редактировать справочники может только администратор." },
        statusCode: StatusCodes.Status403Forbidden);
}

internal sealed record ReferenceNameRequest(string Name);
internal sealed record ReferenceValueRequest(string Name, bool IsArchived);
