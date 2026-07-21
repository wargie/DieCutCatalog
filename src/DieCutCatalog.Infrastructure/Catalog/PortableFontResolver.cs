using PdfSharp.Fonts;

namespace DieCutCatalog.Infrastructure.Catalog;

internal sealed class PortableFontResolver : IFontResolver
{
    private const string RegularFace = "DieCut-Regular";
    private const string BoldFace = "DieCut-Bold";
    private readonly byte[] _regular;
    private readonly byte[] _bold;

    public PortableFontResolver()
    {
        _regular = File.ReadAllBytes(FindFont(false));
        _bold = File.ReadAllBytes(FindFont(true));
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        new(bold ? BoldFace : RegularFace, false, italic);

    public byte[]? GetFont(string faceName) => faceName switch
    {
        RegularFace => _regular,
        BoldFace => _bold,
        _ => null
    };

    private static string FindFont(bool bold)
    {
        var configured = Environment.GetEnvironmentVariable(bold ? "DIECUT_BOLD_FONT_PATH" : "DIECUT_FONT_PATH");
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var candidates = new[]
        {
            configured,
            Path.Combine(windows, bold ? "arialbd.ttf" : "arial.ttf"),
            bold ? "/usr/share/fonts/dejavu/DejaVuSans-Bold.ttf" : "/usr/share/fonts/dejavu/DejaVuSans.ttf",
            bold ? "/usr/share/fonts/ttf/dejavu/DejaVuSans-Bold.ttf" : "/usr/share/fonts/ttf/dejavu/DejaVuSans.ttf",
            bold ? "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" : "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
        };

        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            ?? throw new InvalidOperationException("Не найден шрифт для создания PDF.");
    }
}