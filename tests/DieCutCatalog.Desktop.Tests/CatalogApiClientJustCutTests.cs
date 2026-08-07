using System.Net;
using DieCutCatalog.Application.Catalog;
using Xunit;

namespace DieCutCatalog.Desktop.Tests;

public sealed class CatalogApiClientJustCutTests
{
    [Fact]
    public async Task CalculatePrice_WhenServerRouteIsMissing_ExplainsThatBackendMustBeUpdated()
    {
        using var api = new CatalogApiClient(new HttpClient(new NotFoundHandler()));
        api.Configure("https://catalog.example.com");

        var exception = await Assert.ThrowsAsync<CatalogApiException>(() =>
            api.CalculateJustCutPriceAsync(Guid.NewGuid(), new JustCutPriceParameters()));

        Assert.Equal(
            "На сервере приложения не установлена поддержка JustCUT. Обновите серверную часть до тестовой версии 1.7.0.",
            exception.Message);
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}