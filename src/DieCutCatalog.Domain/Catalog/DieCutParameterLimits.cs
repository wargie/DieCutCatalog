namespace DieCutCatalog.Domain.Catalog;

public static class DieCutParameterLimits
{
    public const int MaximumShaft = 200;
    public const decimal MaximumLabelDimensionMm = 1000m;
    public const int MaximumStreams = 50;
    public const int MaximumRepeats = 100;
    public const decimal MaximumMaterialWidthMm = 520m;
    public const decimal MaximumGrooveSpacingMm = 520m;

    public static string? FindViolation(
        int shaft,
        decimal labelWidth,
        decimal labelLength,
        int streams,
        int repeats,
        decimal materialWidth,
        decimal grooveSpacing,
        decimal cornerRadius)
    {
        if (shaft <= 0 || labelWidth <= 0 || labelLength <= 0)
            return "Вал, L и B должны быть больше нуля.";
        if (streams <= 0 || repeats <= 0)
            return "Количество ручьёв и этикеток в ручье должно быть больше нуля.";
        if (materialWidth <= 0)
            return "Ширина материала должна быть больше нуля.";
        if (grooveSpacing < 0)
            return "Расстояние между ручьями не может быть отрицательным.";
        if (cornerRadius < 0)
            return "Радиус скругления этикетки не может быть отрицательным.";

        if (shaft > MaximumShaft)
            return $"Вал не может превышать {MaximumShaft} зубьев.";
        if (labelWidth > MaximumLabelDimensionMm || labelLength > MaximumLabelDimensionMm)
            return $"Размеры L и B не могут превышать {MaximumLabelDimensionMm:0} мм.";
        if (streams > MaximumStreams)
            return $"Количество ручьёв не может превышать {MaximumStreams}.";
        if (repeats > MaximumRepeats)
            return $"Количество этикеток в ручье не может превышать {MaximumRepeats}.";
        if (materialWidth > MaximumMaterialWidthMm)
            return $"Ширина материала не может превышать {MaximumMaterialWidthMm:0} мм.";
        if (grooveSpacing > MaximumGrooveSpacingMm)
            return $"Расстояние между ручьями не может превышать {MaximumGrooveSpacingMm:0} мм.";
        if (cornerRadius > Math.Min(labelWidth, labelLength) / 2)
            return "Радиус скругления не может превышать половину меньшей стороны этикетки.";

        return null;
    }
}