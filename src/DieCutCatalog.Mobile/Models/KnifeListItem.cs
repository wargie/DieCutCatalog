namespace DieCutCatalog.Mobile.Models;

public sealed record KnifeListItem(
    string Number,
    string Status,
    string StatusColor,
    string Equipment,
    string Material,
    decimal Width,
    decimal Length,
    int Shaft,
    int Streams,
    int Repeats,
    decimal GapYMillimeters,
    long Quantity,
    decimal RunLengthMeters,
    long Revolutions,
    string Figure)
{
    public string Dimensions => $"{Width:0.##} × {Length:0.##}";
}