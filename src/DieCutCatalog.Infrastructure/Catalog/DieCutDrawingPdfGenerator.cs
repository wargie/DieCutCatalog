using System.Globalization;
using DieCutCatalog.Domain.Catalog;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace DieCutCatalog.Infrastructure.Catalog;

internal static class DieCutDrawingPdfGenerator
{
    private const double PointsPerMillimeter = 72d / 25.4d;
    private const double MinimumPageWidthMm = 332.4d;
    private const double MinimumPageHeightMm = 420d;
    private const double SideMarginMm = 17d;
    private const double HeaderHeightMm = 35d;
    private const double BottomMarginMm = 15d;
    private static readonly object FontLock = new();
    private static bool _fontConfigured;

    public static byte[] Generate(DieCut dieCut)
    {
        EnsureFontResolver();

        using var document = new PdfDocument();
        document.Info.Title = $"Die cut {dieCut.Number}";
        document.Info.Subject = "Die cutting layout";
        document.Info.Creator = "DieCut Catalog";
        document.Info.Keywords =
            $"Corner radius = {Format(dieCut.LabelCornerRadius)} mm; vertical break = {Format(dieCut.GrooveSpacing)} mm; " +
            $"horizontal break = {Format(dieCut.GapY * 1000)} mm; streams = {dieCut.Streams}; labels/stream = {dieCut.Repeats}";

        var rapport = dieCut.Shaft * 3.175m;
        var pageWidthMm = Math.Max(MinimumPageWidthMm, (double)dieCut.H + SideMarginMm * 2);
        var pageHeightMm = Math.Max(MinimumPageHeightMm, (double)rapport + HeaderHeightMm + BottomMarginMm);

        var page = document.AddPage();
        page.Width = XUnit.FromMillimeter(pageWidthMm);
        page.Height = XUnit.FromMillimeter(pageHeightMm);

        using var graphics = XGraphics.FromPdfPage(page);
        var regular = new XFont("Arial", 11, XFontStyleEx.Regular);
        var small = new XFont("Arial", 9, XFontStyleEx.Regular);
        var bold = new XFont("Arial", 13, XFontStyleEx.Bold);
        var pen = new XPen(XColor.FromArgb(35, 48, 64), 0.8);
        var textBrush = new XSolidBrush(XColor.FromArgb(28, 35, 43));

        var header = $"{dieCut.Number}, {Format(dieCut.X)}x{Format(dieCut.Y)} mm, Z={dieCut.Shaft}, L={Format(rapport)} mm, H={Format(dieCut.H)} mm";
        graphics.DrawString(header, bold, textBrush, new XRect(35, 30, page.Width.Point - 70, 22), XStringFormats.TopCenter);
        graphics.DrawString(dieCut.Material, regular, textBrush, new XRect(35, 56, page.Width.Point - 70, 20), XStringFormats.TopCenter);
        graphics.DrawString(
            $"Corner radius = {Format(dieCut.LabelCornerRadius)} mm, vertical break = {Format(dieCut.GrooveSpacing)} mm, horizontal break = {Format(dieCut.GapY * 1000)} mm",
            small, textBrush, new XRect(35, 79, page.Width.Point - 70, 16), XStringFormats.TopCenter);

        var frameWidth = MillimetersToPoints(dieCut.H);
        var frameHeight = MillimetersToPoints(rapport);
        var frameX = (page.Width.Point - frameWidth) / 2;
        var frameY = MillimetersToPoints(HeaderHeightMm);
        graphics.DrawRectangle(pen, frameX, frameY, frameWidth, frameHeight);

        var groupWidthMm = dieCut.Streams * dieCut.X + (dieCut.Streams - 1) * dieCut.GrooveSpacing;
        var labelStartMm = (dieCut.H - groupWidthMm) / 2;
        var pitchMm = rapport / dieCut.Repeats;
        var radius = Math.Max(0, MillimetersToPoints(dieCut.LabelCornerRadius));

        for (var row = 0; row < dieCut.Repeats; row++)
        {
            var yMm = row * pitchMm + (pitchMm - dieCut.Y) / 2;
            for (var column = 0; column < dieCut.Streams; column++)
            {
                var xMm = labelStartMm + column * (dieCut.X + dieCut.GrooveSpacing);
                var x = frameX + MillimetersToPoints(xMm);
                var y = frameY + MillimetersToPoints(yMm);
                var width = MillimetersToPoints(dieCut.X);
                var height = MillimetersToPoints(dieCut.Y);
                graphics.DrawRoundedRectangle(pen, x, y, width, height, radius * 2, radius * 2);
            }
        }

        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    private static void EnsureFontResolver()
    {
        lock (FontLock)
        {
            if (_fontConfigured) return;
            GlobalFontSettings.FontResolver = new PortableFontResolver();
            _fontConfigured = true;
        }
    }

    private static string Format(decimal value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static double MillimetersToPoints(decimal value) => (double)value * PointsPerMillimeter;
    private static double MillimetersToPoints(double value) => value * PointsPerMillimeter;
}
