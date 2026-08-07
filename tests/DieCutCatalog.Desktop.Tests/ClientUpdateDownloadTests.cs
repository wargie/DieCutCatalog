using System.Net;
using System.Security.Cryptography;
using DieCutCatalog.Application.Updates;
using DieCutCatalog.Desktop;
using Xunit;

namespace DieCutCatalog.Desktop.Tests;

public sealed class ClientUpdateDownloadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DieCutCatalogDesktopTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DownloadUpdate_FinalizesVerifiedPackageWithoutLeavingTemporaryFile()
    {
        Directory.CreateDirectory(_root);
        var package = RandomNumberGenerator.GetBytes(256 * 1024);
        var manifest = new ClientUpdateManifest(
            "9.9.9",
            "Test update",
            DateTimeOffset.UtcNow,
            "DieCutCatalog-test-win-x64.zip",
            Convert.ToHexString(SHA256.HashData(package)),
            package.Length,
            null);
        var destinationPath = Path.Combine(_root, manifest.FileName);

        using var api = new CatalogApiClient(new HttpClient(new PackageHandler(package)));
        api.Configure("http://127.0.0.1:5080");
        await api.DownloadUpdateAsync(manifest, destinationPath);

        Assert.Equal(package, await File.ReadAllBytesAsync(destinationPath));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.download"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class PackageHandler(byte[] package) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("/api/updates/files/", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(package),
                RequestMessage = request
            });
        }
    }
}