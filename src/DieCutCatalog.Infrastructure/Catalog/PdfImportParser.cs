using System.Globalization;
using System.Text.RegularExpressions;
using DieCutCatalog.Application.Catalog;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace DieCutCatalog.Infrastructure.Catalog;

internal static partial class PdfImportParser
{
    public static PdfImportPreview Parse(Stream content)
    {
        using var document = PdfDocument.Open(content);
        if (document.NumberOfPages == 0) throw new InvalidDataException("PDF не содержит страниц.");

        var pages = document.GetPages().ToArray();
        var text = string.Join("\n", pages.Select(page => ContentOrderTextExtractor.GetText(page)));
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException("В PDF нет распознаваемого текста. Добавьте данные вручную и прикрепите схему к карточке.");

        var number = MatchText(text, @"^\s*([^,\r\n]+)\s*,", RegexOptions.Multiline);
        var dimensions = Regex.Match(text, @"(?<width>\d+(?:[.,]\d+)?)\s*[xх×]\s*(?<length>\d+(?:[.,]\d+)?)\s*mm", RegexOptions.IgnoreCase);
        var shaft = MatchInt(text, @"\bZ\s*=\s*(\d+)");
        var materialWidth = MatchDecimal(text, @"\bH\s*=\s*(\d+(?:[.,]\d+)?)");
        var cornerRadius = MatchDecimal(text, @"Corner\s+radius\s*=\s*(\d+(?:[.,]\d+)?)");
        var grooveSpacing = MatchDecimal(text, @"vertical\s+break\s*=\s*(\d+(?:[.,]\d+)?)");
        var streams = MatchInt(text, @"streams\s*=\s*(\d+)");
        var repeats = MatchInt(text, @"labels/stream\s*=\s*(\d+)");

        decimal? width = dimensions.Success ? ParseDecimal(dimensions.Groups["width"].Value) : null;
        decimal? length = dimensions.Success ? ParseDecimal(dimensions.Groups["length"].Value) : null;
        if ((streams is null || repeats is null) && width is not null && length is not null)
        {
            var inferred = InferGrid(pages[0], width.Value, length.Value);
            streams ??= inferred.Streams;
            repeats ??= inferred.Repeats;
        }
        var material = ExtractMaterial(text);
        var warnings = new List<string>();
        AddMissing(warnings, number, "номер ножа");
        AddMissing(warnings, shaft, "вал (Z)");
        AddMissing(warnings, width, "ширина этикетки");
        AddMissing(warnings, length, "длина этикетки");
        AddMissing(warnings, materialWidth, "ширина материала H");
        AddMissing(warnings, cornerRadius, "радиус скругления");
        AddMissing(warnings, grooveSpacing, "расстояние между ручьями");
        AddMissing(warnings, material, "материал");
        if (streams is null) warnings.Add("Количество ручьёв отсутствует в тексте PDF и требует проверки.");
        if (repeats is null) warnings.Add("Количество этикеток в ручье отсутствует в тексте PDF и требует проверки.");

        return new PdfImportPreview(number, shaft, width, length, streams, repeats, grooveSpacing,
            cornerRadius, material, materialWidth, warnings);
    }

    private static (int? Streams, int? Repeats) InferGrid(UglyToad.PdfPig.Content.Page page, decimal widthMm, decimal lengthMm)
    {
        var expectedWidth = (double)widthMm * 72 / 25.4;
        var expectedHeight = (double)lengthMm * 72 / 25.4;
        var rectangles = page.Paths
            .Select(path => path.GetBoundingRectangle())
            .Where(rectangle => rectangle is not null)
            .Select(rectangle => rectangle!.Value)
            .Where(rectangle =>
                Math.Abs(rectangle.Width - expectedWidth) <= expectedWidth * 0.08
                && Math.Abs(rectangle.Height - expectedHeight) <= expectedHeight * 0.08)
            .ToArray();
        if (rectangles.Length < 2) return (null, null);

        static int CountPositions(IEnumerable<double> values, double tolerance)
        {
            var positions = new List<double>();
            foreach (var value in values.OrderBy(value => value))
            {
                if (positions.All(position => Math.Abs(position - value) > tolerance)) positions.Add(value);
            }
            return positions.Count;
        }

        var streams = CountPositions(rectangles.Select(rectangle => rectangle.Left + rectangle.Width / 2), expectedWidth * 0.1);
        var repeats = CountPositions(rectangles.Select(rectangle => rectangle.Bottom + rectangle.Height / 2), expectedHeight * 0.1);
        return streams * repeats == rectangles.Length ? (streams, repeats) : (null, null);
    }
    private static string? ExtractMaterial(string text)
    {
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => Regex.Replace(line, @"\s+", " ").Trim())
            .Where(line => line.Length > 0)
            .ToArray();
        var cornerIndex = Array.FindIndex(lines, line => line.Contains("Corner radius", StringComparison.OrdinalIgnoreCase));
        if (cornerIndex > 1)
        {
            var candidate = lines[cornerIndex - 1];
            if (!candidate.Contains("streams =", StringComparison.OrdinalIgnoreCase)) return candidate;
            if (cornerIndex > 2) return lines[cornerIndex - 2];
        }

        var match = Regex.Match(text,
            @"\bH\s*=\s*\d+(?:[.,]\d+)?(?:\s*mm)?\s*(?<material>.+?)\s*Corner\s+radius",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? Regex.Replace(match.Groups["material"].Value, @"\s+", " ").Trim() : null;
    }

    private static string? MatchText(string text, string pattern, RegexOptions options = RegexOptions.None)
    {
        var match = Regex.Match(text, pattern, options);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static int? MatchInt(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : null;
    }

    private static decimal? MatchDecimal(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        return match.Success ? ParseDecimal(match.Groups[1].Value) : null;
    }

    private static decimal? ParseDecimal(string value) =>
        decimal.TryParse(value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    private static void AddMissing<T>(ICollection<string> warnings, T? value, string field) where T : struct
    {
        if (value is null) warnings.Add($"Не удалось распознать поле «{field}».");
    }

    private static void AddMissing(ICollection<string> warnings, string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) warnings.Add($"Не удалось распознать поле «{field}».");
    }
}