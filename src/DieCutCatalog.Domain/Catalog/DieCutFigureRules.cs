namespace DieCutCatalog.Domain.Catalog;

public enum ParametricPdfContour
{
    RoundedRectangle,
    Circle,
    Unsupported
}

public static class DieCutFigureRules
{
    public static ParametricPdfContour ResolvePdfContour(string? figure) =>
        Normalize(figure) switch
        {
            "прямоугольник" or "rectangle" or "квадрат" or "square" =>
                ParametricPdfContour.RoundedRectangle,
            "круг" or "окружность" or "circle" or "round" =>
                ParametricPdfContour.Circle,
            _ => ParametricPdfContour.Unsupported
        };

    public static string? FindPdfGenerationViolation(string? figure, decimal labelWidth, decimal labelLength)
    {
        var normalized = Normalize(figure);
        if (normalized is "круг" or "окружность" or "circle" or "round" && labelWidth != labelLength)
            return "для круглой этикетки размеры L и B должны совпадать";
        if (normalized is "квадрат" or "square" && labelWidth != labelLength)
            return "для квадратной этикетки размеры L и B должны совпадать";
        if (ResolvePdfContour(figure) != ParametricPdfContour.Unsupported) return null;

        var displayName = string.IsNullOrWhiteSpace(figure) ? "не указана" : $"«{figure.Trim()}»";
        return $"параметрическое построение фигуры {displayName} не поддерживается; загрузите утверждённый PDF-чертёж с реальным контуром";
    }

    private static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;
}
