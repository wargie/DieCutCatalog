using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using DieCutCatalog.Application.Updates;

namespace DieCutCatalog.Desktop;

internal sealed partial class CatalogApiClient
{
    private const long MaxUpdatePackageSize = 512L * 1024 * 1024;
    public async Task<ClientUpdateManifest?> GetLatestUpdateAsync()
    {
        using var request = CreateRequest(HttpMethod.Get, "api/updates/latest", authorize: false);
        using var response = await SendCoreAsync(request);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ClientUpdateManifest>(_jsonOptions)
            ?? throw new CatalogApiException("Сервер вернул пустой манифест обновления.");
    }

    public async Task DownloadUpdateAsync(
        ClientUpdateManifest manifest,
        string destinationPath,
        IProgress<ClientUpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidSha256(manifest.Sha256)
            || !string.Equals(manifest.FileName, Path.GetFileName(manifest.FileName), StringComparison.Ordinal)
            || manifest.Size <= 0
            || manifest.Size > MaxUpdatePackageSize)
        {
            throw new CatalogApiException("Сервер вернул некорректный манифест обновления.");
        }

        if (await IsPackageValidAsync(destinationPath, manifest, cancellationToken))
        {
            progress?.Report(new ClientUpdateDownloadProgress(manifest.Size, manifest.Size));
            return;
        }

        var temporaryPath = destinationPath + $".{Guid.NewGuid():N}.download";
        try
        {
            using var request = CreateRequest(HttpMethod.Get, $"api/updates/files/{Uri.EscapeDataString(manifest.FileName)}", authorize: false);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            await EnsureSuccessAsync(response);

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long received = 0;
                progress?.Report(new ClientUpdateDownloadProgress(received, manifest.Size));
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    if (received > manifest.Size)
                        throw new CatalogApiException("Размер загружаемого обновления превышает опубликованный.");
                    progress?.Report(new ClientUpdateDownloadProgress(received, manifest.Size));
                }
            }

            if (new FileInfo(temporaryPath).Length != manifest.Size)
            {
                throw new CatalogApiException("Размер загруженного обновления не совпадает с опубликованным.");
            }

            await using var verificationStream = File.OpenRead(temporaryPath);
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(verificationStream, cancellationToken));
            if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new CatalogApiException("Контрольная сумма обновления не совпадает. Файл удалён.");
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        catch (HttpRequestException exception)
        {
            throw new CatalogApiException("Не удалось загрузить обновление с сервера.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CatalogApiException("Сервер не ответил вовремя.", exception);
        }
        catch (IOException exception)
        {
            throw new CatalogApiException(
                "Не удалось сохранить пакет обновления. Закройте другие окна DieCut Catalog и повторите попытку.",
                exception);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static async Task<bool> IsPackageValidAsync(
        string path,
        ClientUpdateManifest manifest,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != manifest.Size) return false;

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            return string.Equals(hash, manifest.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
    }
    private static bool IsValidSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}
