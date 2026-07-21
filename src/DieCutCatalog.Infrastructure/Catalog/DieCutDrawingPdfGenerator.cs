using System.Globalization;
using DieCutCatalog.Domain.Catalog;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace DieCutCatalog.Infrastructure.Catalog;

internal static class DieCutDrawingPdfGenerator
{
    private static readonly object FontLock = new();
    private static bool _fontConfigured;

    public static byte[] Generate(DieCut dieCut)
    {
        EnsureFontResolver();

        using var document = new PdfDocument();
        document.Info.Title = $"Die cut {dieCut.Number}";
        document.Info.Subject = "Die cutting layout";
        document.Info.Creator = "DieCut Catalog";

        var page = document.AddPage();
        page.Width = XUnit.FromMillimeter(332.4);
        page.Height = XUnit.FromMillimeter(420);

        using var graphics = XGraphics.FromPdfPage(page);
        var regular = new XFont("DieCut Sans", 11, XFontStyleEx.Regular);
        var small = new XFont("DieCut Sans", 9, XFontStyleEx.Regular);
        var bold = new XFont("DieCut Sans", 13, XFontStyleEx.Bold);
        var pen = new XPen(XColor.FromArgb(35, 48, 64), 0.8);
        var lightPen = new XPen(XColor.FromArgb(120, 130, 140), 0.45);
        var textBrush = new XSolidBrush(XColor.FromArgb(28, 35, 43));

        var rapport = dieCut.Shaft * 3.175m;
        var header = $"{dieCut.Number}, {Format(dieCut.X)}x{Format(dieCut.Y)} mm, Z={dieCut.Shaft}, rapport={Format(rapport)} mm, H={Format(dieCut.H)} mm";
        graphics.DrawString(header, bold, textBrush, new XRect(35, 30, page.Width.Point - 70, 22), XStringFormats.TopCenter);
        graphics.DrawString(dieCut.Material, regular, textBrush, new XRect(35, 56, page.Width.Point - 70, 20), XStringFormats.TopCenter);
        graphics.DrawString(
            $"Corner radius = {Format(dieCut.LabelCornerRadius)} mm, vertical break = {Format(dieCut.GrooveSpacing)} mm, horizontal break = {Format(dieCut.GapY * 1000)} mm",
            regular, textBrush, new XRect(35, 80, page.Width.Point - 70, 20), XStringFormats.TopCenter);
        graphics.DrawString(
            $"streams = {dieCut.Streams}, labels/stream = {dieCut.Repeats}, equipment = {dieCut.Equipment.Name}",
            small, textBrush, new XRect(35, 103, page.Width.Point - 70, 18), XStringFormats.TopCenter);

        const double drawingTop = 145;
        const double sideMargin = 48;
        const double bottomMargin = 55;
        var availableWidth = page.Width.Point - sideMargin * 2;
        var availableHeight = page.Height.Point - drawingTop - bottomMargin;
        var scale = Math.Min(
            availableWidth / (double)dieCut.H,
            availableHeight / (double)rapport);

        var frameWidth = (double)dieCut.H * scale;
        var frameHeight = (double)rapport * scale;
        var frameX = (page.Width.Point - frameWidth) / 2;
        var frameY = drawingTop + (availableHeight - frameHeight) / 2;
        graphics.DrawRectangle(pen, frameX, frameY, frameWidth, frameHeight);

        var groupWidthMm = dieCut.Streams * dieCut.X + (dieCut.Streams - 1) * dieCut.GrooveSpacing;
        var labelStartMm = (dieCut.H - groupWidthMm) / 2;
        var pitchMm = rapport / dieCut.Repeats;
        var radius = Math.Max(0, (double)dieCut.LabelCornerRadius * scale);

        for (var row = 0; row < dieCut.Repeats; row++)
        {
            var yMm = row * pitchMm + (pitchMm - dieCut.Y) / 2;
            for (var column = 0; column < dieCut.Streams; column++)
            {
                var xMm = labelStartMm + column * (dieCut.X + dieCut.GrooveSpacing);
                var x = frameX + (double)xMm * scale;
                var y = frameY + (double)yMm * scale;
                var width = (double)dieCut.X * scale;
                var height = (double)dieCut.Y * scale;
                graphics.DrawRoundedRectangle(pen, x, y, width, height, radius * 2, radius * 2);
            }
        }

        graphics.DrawLine(lightPen, frameX, frameY - 12, frameX + frameWidth, frameY - 12);
        graphics.DrawString($"Material width {Format(dieCut.H)} mm", small, textBrush,
            new XRect(frameX, frameY - 28, frameWidth, 14), XStringFormats.TopCenter);
        graphics.DrawString($"Rapport {Format(rapport)} mm", small, textBrush,
            new XRect(frameX, frameY + frameHeight + 8, frameWidth, 16), XStringFormats.TopCenter);

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
}