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
        endpoints.MapGet("/api/updates/latest", async (IOptions<StorageOptions> storageOptions, CancellationToken cancellationToken) =>
        {
            var published = await ReadWindowsUpdateAsync(storageOptions.Value, cancellationToken);
            return published is null ? Results.NoContent() : Results.Ok(published.Value.Manifest);
        });

        endpoints.MapGet("/api/updates/files/{fileName}", async (string fileName, IOptions<StorageOptions> storageOptions, CancellationToken cancellationToken) =>
        {
            var published = await ReadWindowsUpdateAsync(storageOptions.Value, cancellationToken);
            return published is null || !IsRequestedFile(fileName, published.Value.Manifest.FileName)
                ? Results.NotFound()
                : Results.File(published.Value.PackagePath, "application/zip", fileDownloadName: published.Value.Manifest.FileName, enableRangeProcessing: true);
        });

        endpoints.MapGet("/api/updates/android/latest", async (IOptions<StorageOptions> storageOptions, CancellationToken cancellationToken) =>
        {
            var published = await ReadAndroidUpdateAsync(storageOptions.Value, cancellationToken);
            return published is null ? Results.NoContent() : Results.Ok(published.Value.Manifest);
        });

        endpoints.MapGet("/api/updates/android/files/{fileName}", async (string fileName, IOptions<StorageOptions> storageOptions, CancellationToken cancellationToken) =>
        {
            var published = await ReadAndroidUpdateAsync(storageOptions.Value, cancellationToken);
            return published is null || !IsRequestedFile(fileName, published.Value.Manifest.FileName)
                ? Results.NotFound()
                : Results.File(published.Value.PackagePath, "application/vnd.android.package-archive", fileDownloadName: published.Value.Manifest.FileName, enableRangeProcessing: true);
        });

        return endpoints;
    }

    private static async Task<(ClientUpdateManifest Manifest, string PackagePath)?> ReadWindowsUpdateAsync(StorageOptions options, CancellationToken cancellationToken)
    {
        // Keep the legacy root as a fallback for clients published before platform channels.
        var platformRoot = Path.GetFullPath(Path.Combine(options.RootPath, "updates", "windows"));
        var legacyRoot = Path.GetFullPath(Path.Combine(options.RootPath, "updates"));
        return await ReadUpdateAsync<ClientUpdateManifest>(platformRoot, ".zip", IsValidWindowsManifest, cancellationToken)
            ?? await ReadUpdateAsync<ClientUpdateManifest>(legacyRoot, ".zip", IsValidWindowsManifest, cancellationToken);
    }

    private static Task<(AndroidUpdateManifest Manifest, string PackagePath)?> ReadAndroidUpdateAsync(StorageOptions options, CancellationToken cancellationToken) =>
        ReadUpdateAsync<AndroidUpdateManifest>(
            Path.GetFullPath(Path.Combine(options.RootPath, "updates", "android")),
            ".apk",
            manifest => manifest.VersionCode > 0
                && !string.IsNullOrWhiteSpace(manifest.ReleaseName)
                && IsValidCommonManifest(manifest.Version, manifest.FileName, manifest.Sha256, manifest.Size),
            cancellationToken);

    private static async Task<(T Manifest, string PackagePath)?> ReadUpdateAsync<T>(
        string updateRoot,
        string requiredExtension,
        Func<T, bool> isValid,
        CancellationToken cancellationToken) where T : class
    {
        var manifestPath = Path.Combine(updateRoot, "latest.json");
        if (!File.Exists(manifestPath)) return null;

        T? manifest;
        try
        {
            await using var stream = File.OpenRead(manifestPath);
            manifest = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }

        if (manifest is null || !isValid(manifest)) return null;

        var (fileName, size) = manifest switch
        {
            ClientUpdateManifest windows => (windows.FileName, windows.Size),
            AndroidUpdateManifest android => (android.FileName, android.Size),
            _ => (string.Empty, 0L)
        };

        if (!fileName.EndsWith(requiredExtension, StringComparison.OrdinalIgnoreCase)) return null;

        var packagePath = Path.GetFullPath(Path.Combine(updateRoot, fileName));
        var rootPrefix = updateRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!packagePath.StartsWith(rootPrefix, StringComparison.Ordinal)
            || !File.Exists(packagePath)
            || new FileInfo(packagePath).Length != size)
        {
            return null;
        }

        return (manifest, packagePath);
    }

    private static bool IsValidWindowsManifest(ClientUpdateManifest manifest) =>
        !string.IsNullOrWhiteSpace(manifest.ReleaseName)
        && IsValidCommonManifest(manifest.Version, manifest.FileName, manifest.Sha256, manifest.Size);

    private static bool IsValidCommonManifest(string version, string fileName, string sha256, long size) =>
        !string.IsNullOrWhiteSpace(version)
        && !string.IsNullOrWhiteSpace(fileName)
        && !string.IsNullOrWhiteSpace(sha256)
        && sha256.Length == 64
        && sha256.All(Uri.IsHexDigit)
        && size > 0
        && string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal);

    private static bool IsRequestedFile(string requested, string published) =>
        string.Equals(requested, published, StringComparison.Ordinal)
        && string.Equals(requested, Path.GetFileName(requested), StringComparison.Ordinal);
}
