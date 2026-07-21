namespace DieCutCatalog.Domain.Catalog;

public static class DieCutCalculations
{
    public const decimal ShaftPitchMm = 3.175m;

    public static decimal CalculateGapX(decimal h, decimal x, int streams) =>
        (h - x * streams) / 1000m;

    public static decimal CalculateGapY(int shaft, decimal y, int repeats) =>
        ((shaft * ShaftPitchMm / repeats) - y) / 1000m;

    public static (decimal GapX, decimal GapY) Calculate(
        int shaft,
        decimal x,
        decimal y,
        int streams,
        int repeats,
        decimal h) =>
        (CalculateGapX(h, x, streams), CalculateGapY(shaft, y, repeats));
}
