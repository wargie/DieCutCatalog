using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DieCutCatalog.Application.Catalog;
using Microsoft.Extensions.Options;

namespace DieCutCatalog.Infrastructure.JustCut;

public sealed class JustCutService(
    HttpClient httpClient,
    IOptions<JustCutOptions> options,
    IDieCutCatalogService catalog) : IJustCutService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly JustCutOptions _options = options.Value;

    public async Task<JustCutPriceResult?> CalculatePriceAsync(
        Guid dieCutId,
        JustCutPriceParameters parameters,
        CancellationToken cancellationToken = default)
    {
        ValidateConfiguration();
        ValidateParameters(parameters);

        var dieCut = await catalog.GetAsync(dieCutId, cancellationToken);
        if (dieCut is null) return null;

        var payload = BuildPayload(dieCut, parameters);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(EnsureTrailingSlash(_options.BaseUrl)), "requestpriceknife"))
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var result = await response.Content.ReadFromJsonAsync<JustCutResponse>(JsonOptions, cancellationToken);
            if (result is null)
                throw new JustCutIntegrationException("JustCut вернул пустой или некорректный ответ.");
            if (!response.IsSuccessStatusCode || !string.IsNullOrWhiteSpace(result.ErrorText))
                throw new JustCutIntegrationException(
                    string.IsNullOrWhiteSpace(result.ErrorText)
                        ? $"JustCut отклонил запрос (HTTP {(int)response.StatusCode})."
                        : result.ErrorText);
            if (result.SumOrder <= 0)
                throw new JustCutIntegrationException("JustCut не смог рассчитать стоимость для заданных параметров.");

            return new JustCutPriceResult(
                result.SumOrder,
                "RUB",
                true,
                result.NumberOrder,
                DateTimeOffset.UtcNow,
                _options.Environment);
        }
        catch (JustCutIntegrationException)
        {
            throw;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new JustCutIntegrationException("JustCut не ответил за отведённое время.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new JustCutIntegrationException("Не удалось подключиться к сервису JustCut.", exception);
        }
        catch (JsonException exception)
        {
            throw new JustCutIntegrationException("JustCut вернул ответ неизвестного формата.", exception);
        }
    }

    private object BuildPayload(DieCutDetails dieCut, JustCutPriceParameters parameters)
    {
        var figure = dieCut.Figure.Trim().ToLowerInvariant();
        var isCircle = figure is "круг" or "окружность" or "circle" or "round";
        var isRectangle = figure is "прямоугольник" or "квадрат" or "rectangle" or "square";
        if (!isCircle && !isRectangle)
            throw new ValidationException(
                "Расчёт JustCut пока поддерживает прямоугольные, квадратные и круглые ножи.");

        var common = new Dictionary<string, object?>
        {
            ["uidcontragent"] = _options.UidContragent,
            ["numberorder"] = 0,
            ["typeorder"] = 0,
            ["nameorder"] = string.Empty,
            ["rushorder"] = parameters.RushOrder ? 1 : 0,
            ["shippingdate"] = string.Empty,
            ["numberrepeatorder"] = 0,
            ["comments"] = $"Расчёт из DieCut Catalog: нож {dieCut.Number}",
            ["delivery"] = 0,
            ["deliveryaddress"] = string.Empty,
            ["typecircuit"] = isCircle ? 1 : 2,
            ["typefelling"] = 1,
            ["shaftpitch"] = decimal.Round(dieCut.Shaft * 3.175m, 3),
            ["knifelength"] = 0,
            ["knifewidth"] = dieCut.H,
            ["knifeheight"] = parameters.KnifeHeight,
            ["substratethickness"] = parameters.SubstrateThickness,
            ["anglesharpening"] = parameters.AngleSharpening,
            ["edge2mm"] = parameters.EdgeUnder2Mm ? 1 : 0,
            ["antiadhesioncoating"] = parameters.AntiAdhesionCoating ? 1 : 0,
            ["laserhardening"] = parameters.LaserHardening ? 1 : 0,
            // Поле обязательно в фактическом API, хотя отсутствует в выданной инструкции.
            ["hardeningcoating"] = parameters.HardeningCoating ? 1 : 0,
            ["manualknifewidth"] = 1,
            ["amountrepetitionsrapport"] = dieCut.Repeats,
            ["distancerepetitionsrapport"] = 0,
            ["amountstreams"] = dieCut.Streams,
            ["distancestreams"] = dieCut.Streams > 1 ? dieCut.GrooveSpacing : 0
        };

        if (isCircle)
        {
            if (decimal.Abs(dieCut.X - dieCut.Y) > 0.01m)
                throw new ValidationException("Для круглого ножа размеры X и Y должны совпадать.");
            common["diameter"] = dieCut.X;
        }
        else
        {
            common["width"] = dieCut.X;
            common["length"] = dieCut.Y;
            common["radiusfillet"] = dieCut.LabelCornerRadius;
        }

        return common;
    }

    private void ValidateConfiguration()
    {
        if (!Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
            throw new JustCutIntegrationException("Адрес сервиса JustCut не настроен.");
        if (!Guid.TryParse(_options.UidContragent, out _))
            throw new JustCutIntegrationException("Идентификатор контрагента JustCut не настроен.");
    }

    private static void ValidateParameters(JustCutPriceParameters parameters)
    {
        if (parameters.KnifeHeight is < 0.300m or > 2m)
            throw new ValidationException("Высота ножа должна быть от 0,300 до 2 мм.");
        if (parameters.SubstrateThickness is < 0.010m or > 0.200m)
            throw new ValidationException("Толщина подложки должна быть от 0,010 до 0,200 мм.");
        if (parameters.AngleSharpening is < 50 or > 180)
            throw new ValidationException("Угол заточки должен быть от 50 до 180 градусов.");
    }

    private static string EnsureTrailingSlash(string value) => value.EndsWith('/') ? value : value + "/";

    private sealed record JustCutResponse(
        [property: JsonPropertyName("numberorder")] long NumberOrder,
        [property: JsonPropertyName("sumorder")] decimal SumOrder,
        [property: JsonPropertyName("errortext")] string ErrorText);
}
