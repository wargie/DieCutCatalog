namespace DieCutCatalog.Domain.Catalog;

public static class DieCutCalculations
{
    public const decimal ShaftPitchMm = 3.175m;

    public static decimal CalculateLayoutWidth(
        decimal labelLength,
        int grooves,
        decimal grooveSpacing) =>
        labelLength * grooves + grooveSpacing * Math.Max(0, grooves - 1);

    public static decimal CalculateA1(
        decimal materialWidth,
        decimal labelLength,
        int grooves,
        decimal grooveSpacing) =>
        (materialWidth - CalculateLayoutWidth(labelLength, grooves, grooveSpacing)) / 1000m;

    public static decimal CalculateA2(int shaft, decimal labelWidth, int labelsPerGroove) =>
        ((shaft * ShaftPitchMm / labelsPerGroove) - labelWidth) / 1000m;

    public static (decimal RunLengthMeters, long Revolutions) CalculateRunMetrics(
        long quantity,
        int streams,
        decimal labelLengthMm,
        decimal interLabelSpacingMeters,
        int shaft)
    {
        var runLengthMeters = quantity / (decimal)streams * (labelLengthMm / 1000m + interLabelSpacingMeters);
        var rapportLengthMeters = shaft * ShaftPitchMm / 1000m;
        return (runLengthMeters, checked((long)decimal.Ceiling(runLengthMeters / rapportLengthMeters)));
    }

    public static (decimal A1, decimal A2) Calculate(
        int shaft,
        decimal labelLength,
        decimal labelWidth,
        int grooves,
        int labelsPerGroove,
        decimal materialWidth,
        decimal grooveSpacing) =>
        (CalculateA1(materialWidth, labelLength, grooves, grooveSpacing),
            CalculateA2(shaft, labelWidth, labelsPerGroove));
}
