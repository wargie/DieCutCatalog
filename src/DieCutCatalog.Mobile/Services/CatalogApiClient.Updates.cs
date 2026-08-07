using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using DieCutCatalog.Mobile.Models;

namespace DieCutCatalog.Mobile.Services;

public sealed partial class CatalogApiClient
{
    private const long MaximumApkSize = 256L * 1024 * 1024;

    public async Task<AndroidUpdateManifest?> GetLatestAndroidUpdateAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/updates/android/latest");
        using var response = await SendUpdateRequestAsync(request, HttpCompletionOption.ResponseContentRead);
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        await EnsureUpdateSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<AndroidUpdateManifest>(JsonOptions)
            ?? throw new ApiException("Сервер вернул пустой манифест Android-обновления.");
    }

    public async Task<string> DownloadAndroidUpdateAsync(
        AndroidUpdateManifest manifest,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidateUpdateManifest(manifest);
        var destinationPath = Path.Combine(FileSystem.CacheDirectory, manifest.FileName);
        if (await IsValidApkAsync(destinationPath, manifest, cancellationToken))
        {
            progress?.Report(new UpdateDownloadProgress(manifest.Size, manifest.Size));
            return destinationPath;
        }

        var temporaryPath = destinationPath + $".{Guid.NewGuid():N}.download";
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"api/updates/android/files/{Uri.EscapeDataString(manifest.FileName)}");
            using var response = await SendUpdateRequestAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            await EnsureUpdateSuccessAsync(response);

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true);

            var buffer = new byte[81920];
            long received = 0;
            progress?.Report(new UpdateDownloadProgress(0, manifest.Size));
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                if (received > manifest.Size)
                    throw new ApiException("Размер APK превышает опубликованный.");
                progress?.Report(new UpdateDownloadProgress(received, manifest.Size));
            }

            await destination.FlushAsync(cancellationToken);
            if (new FileInfo(temporaryPath).Length != manifest.Size)
                throw new ApiException("Размер загруженного APK не совпадает с опубликованным.");

            await VerifySha256Async(temporaryPath, manifest.Sha256, cancellationToken);
            File.Move(temporaryPath, destinationPath, overwrite: true);
            return destinationPath;
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (IOException) { }
        }
    }

    private async Task<HttpResponseMessage> SendUpdateRequestAsync(
        HttpRequestMessage request,
        HttpCompletionOption option,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await http.SendAsync(request, option, cancellationToken);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ApiException("Сервер не ответил вовремя.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new ApiException("Не удалось подключиться к серверу обновлений.", exception);
        }
    }

    private static async Task EnsureUpdateSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var message = await ReadErrorAsync(response) ?? $"Сервер отклонил запрос обновления ({(int)response.StatusCode}).";
        throw new ApiException(message);
    }

    private static void ValidateUpdateManifest(AndroidUpdateManifest manifest)
    {
        if (manifest.VersionCode <= 0
            || manifest.Size <= 0
            || manifest.Size > MaximumApkSize
            || manifest.Sha256.Length != 64
            || !manifest.Sha256.All(Uri.IsHexDigit)
            || !manifest.FileName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(manifest.FileName, Path.GetFileName(manifest.FileName), StringComparison.Ordinal))
        {
            throw new ApiException("Сервер вернул некорректный манифест Android-обновления.");
        }
    }

    private static async Task<bool> IsValidApkAsync(
        string path,
        AndroidUpdateManifest manifest,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != manifest.Size) return false;
        try
        {
            await VerifySha256Async(path, manifest.Sha256, cancellationToken);
            return true;
        }
        catch (ApiException)
        {
            return false;
        }
    }

    private static async Task VerifySha256Async(
        string path,
        string expected,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new ApiException("Контрольная сумма APK не совпадает. Файл удалён.");
    }
}
