using System.Text.Json;
using DieCutCatalog.Application.Updates;
using DieCutCatalog.Infrastructure.Employees;
using Microsoft.Extensions.Options;

namespace DieCutCatalog.Api;

public static class UpdateEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapUpdateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/updates/latest", async (
            IOptions<StorageOptions> storageOptions,
            CancellationToken cancellationToken) =>
        {
            var published = await ReadPublishedUpdateAsync(storageOptions.Value, cancellationToken);
            return published is null ? Results.NoContent() : Results.Ok(published.Value.Manifest);
        });

        endpoints.MapGet("/api/updates/files/{fileName}", async (
            string fileName,
            IOptions<StorageOptions> storageOptions,
            CancellationToken cancellationToken) =>
        {
            var published = await ReadPublishedUpdateAsync(storageOptions.Value, cancellationToken);
            if (published is null
                || !string.Equals(fileName, published.Value.Manifest.FileName, StringComparison.Ordinal)
                || !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
            {
                return Results.NotFound();
            }

            return Results.File(
                published.Value.PackagePath,
                "application/zip",
                fileDownloadName: published.Value.Manifest.FileName,
                enableRangeProcessing: true);
        });

        return endpoints;
    }

    private static async Task<(ClientUpdateManifest Manifest, string PackagePath)?> ReadPublishedUpdateAsync(
        StorageOptions options,
        CancellationToken cancellationToken)
    {
        var updateRoot = Path.GetFullPath(Path.Combine(options.RootPath, "updates"));
        var manifestPath = Path.Combine(updateRoot, "latest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(manifestPath);
        ClientUpdateManifest? manifest;
        try
        {
            manifest = await JsonSerializer.DeserializeAsync<ClientUpdateManifest>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }

        if (manifest is null
            || string.IsNullOrWhiteSpace(manifest.Version)
            || string.IsNullOrWhiteSpace(manifest.FileName)
            || string.IsNullOrWhiteSpace(manifest.Sha256)
            || manifest.Size <= 0
            || !string.Equals(manifest.FileName, Path.GetFileName(manifest.FileName), StringComparison.Ordinal))
        {
            return null;
        }

        var packagePath = Path.GetFullPath(Path.Combine(updateRoot, manifest.FileName));
        if (!packagePath.StartsWith(updateRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !File.Exists(packagePath)
            || new FileInfo(packagePath).Length != manifest.Size)
        {
            return null;
        }

        return (manifest, packagePath);
    }
}