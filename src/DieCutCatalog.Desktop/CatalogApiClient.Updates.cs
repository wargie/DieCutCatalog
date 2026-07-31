using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using DieCutCatalog.Application.Updates;

namespace DieCutCatalog.Desktop;

internal sealed partial class CatalogApiClient
{
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

    public async Task DownloadUpdateAsync(ClientUpdateManifest manifest, string destinationPath, CancellationToken cancellationToken = default)
    {
        if (!IsValidSha256(manifest.Sha256)
            || !string.Equals(manifest.FileName, Path.GetFileName(manifest.FileName), StringComparison.Ordinal))
        {
            throw new CatalogApiException("Сервер вернул некорректный манифест обновления.");
        }

        var temporaryPath = destinationPath + ".download";
        try
        {
            using var request = CreateRequest(HttpMethod.Get, $"api/updates/files/{Uri.EscapeDataString(manifest.FileName)}", authorize: false);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            await EnsureSuccessAsync(response);

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await source.CopyToAsync(destination, cancellationToken);
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
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static bool IsValidSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}