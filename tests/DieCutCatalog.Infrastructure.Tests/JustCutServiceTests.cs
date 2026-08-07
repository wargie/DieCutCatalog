using System.Net;
using System.Text;
using System.Text.Json;
using DieCutCatalog.Application.Catalog;
using DieCutCatalog.Domain.Catalog;
using DieCutCatalog.Infrastructure.JustCut;
using Microsoft.Extensions.Options;

namespace DieCutCatalog.Infrastructure.Tests;

public sealed class JustCutServiceTests
{
    private const string Uid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    [Fact]
    public async Task CalculatePrice_BuildsSafePriceOnlyRequestAndReturnsAmount()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK,
            "{\"numberorder\":0,\"sumorder\":16496,\"errortext\":\"\"}");
        var service = CreateService(handler, Rectangle());

        var result = await service.CalculatePriceAsync(RectangleId, new JustCutPriceParameters());

        Assert.NotNull(result);
        Assert.Equal(16496m, result.Amount);
        Assert.True(result.IncludesVat);
        Assert.Equal("RUB", result.Currency);
        using var payload = JsonDocument.Parse(handler.RequestBody!);
        var root = payload.RootElement;
        Assert.Equal(Uid, root.GetProperty("uidcontragent").GetString());
        Assert.Equal(0, root.GetProperty("typeorder").GetInt32());
        Assert.Equal(254m, root.GetProperty("shaftpitch").GetDecimal());
        Assert.Equal(50m, root.GetProperty("width").GetDecimal());
        Assert.Equal(80m, root.GetProperty("length").GetDecimal());
        Assert.Equal(4, root.GetProperty("amountstreams").GetInt32());
        Assert.Equal(0, root.GetProperty("hardeningcoating").GetInt32());
    }

    [Fact]
    public async Task CalculatePrice_UsesErrorTextFromJustCut()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest,
            "{\"numberorder\":0,\"sumorder\":0,\"errortext\":\"Некорректные параметры\"}");
        var service = CreateService(handler, Rectangle());

        var exception = await Assert.ThrowsAsync<JustCutIntegrationException>(() =>
            service.CalculatePriceAsync(RectangleId, new JustCutPriceParameters()));

        Assert.Equal("Некорректные параметры", exception.Message);
    }

    private static JustCutService CreateService(HttpMessageHandler handler, DieCutDetails details) =>
        new(
            new HttpClient(handler),
            Options.Create(new JustCutOptions
            {
                BaseUrl = "http://api1c.justcut.ru:8081/jctest/hs/jcexch/",
                UidContragent = Uid,
                Environment = "Test"
            }),
            new StubCatalog(details));

    private static readonly Guid RectangleId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static DieCutDetails Rectangle() => new(
        RectangleId, "016LS", null, "Label Source", 80, 50m, 80m, 4, 3,
        2m, 2m, 0m, 0m, "PP white", 330m, "прямоугольник", null,
        DateOnly.FromDateTime(DateTime.Today), 0, 0m, 0, 0, 0m, 0, 1,
        500_000, DieCutStatus.Active, null, null, null, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class RecordingHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class StubCatalog(DieCutDetails details) : IDieCutCatalogService
    {
        public Task<DieCutDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<DieCutDetails?>(id == details.Id ? details : null);

        public Task<PagedResult<DieCutSummary>> SearchAsync(DieCutQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DieCutDetails> CreateAsync(SaveDieCutCommand command, Guid employeeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DieCutDetails?> UpdateAsync(Guid id, SaveDieCutCommand command, Guid employeeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DieCutDetails?> AddCirculationAsync(Guid id, long? quantity, decimal? runLengthMeters, Guid employeeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DieCutDetails?> InstallReplacementAsync(Guid id, Guid employeeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DieCutDetails?> RetireAsync(Guid id, Guid employeeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DieCutDetails?> DeleteAsync(Guid id, Guid employeeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DieCutEventDetails>?> GetEventsAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CatalogFacets> GetFacetsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
