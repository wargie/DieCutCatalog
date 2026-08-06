namespace DieCutCatalog.Application.Catalog;

public sealed record JustCutPriceParameters(
    bool RushOrder = false,
    decimal KnifeHeight = 0.442m,
    decimal SubstrateThickness = 0.055m,
    int AngleSharpening = 90,
    bool EdgeUnder2Mm = false,
    bool AntiAdhesionCoating = false,
    bool LaserHardening = false,
    bool HardeningCoating = false);

public sealed record JustCutPriceResult(
    decimal Amount,
    string Currency,
    bool IncludesVat,
    long NumberOrder,
    DateTimeOffset CalculatedAt,
    string Environment);

public interface IJustCutService
{
    Task<JustCutPriceResult?> CalculatePriceAsync(
        Guid dieCutId,
        JustCutPriceParameters parameters,
        CancellationToken cancellationToken = default);
}

public sealed class JustCutIntegrationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
