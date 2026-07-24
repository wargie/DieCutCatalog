using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DieCutCatalog.Application.Catalog;
using DieCutCatalog.Domain.Catalog;
using DieCutCatalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DieCutCatalog.Infrastructure.Catalog;

public sealed class ExcelCatalogImportService(CatalogDbContext dbContext) : IExcelCatalogImportService
{
    public async Task<ExcelImportPreview> PreviewAsync(Stream content, CancellationToken cancellationToken = default)
    {
        var parsed = Parse(content);
        var existingKeys = (await dbContext.DieCuts.AsNoTracking()
            .Select(x => x.Equipment.NormalizedName + "\n" + x.NormalizedNumber)
            .ToListAsync(cancellationToken)).ToHashSet();
        var existing = parsed.Rows.Count(x => existingKeys.Contains(Key(x.Equipment, x.Number)));
        return new ExcelImportPreview(
            parsed.TotalRows,
            parsed.Rows.Count,
            parsed.Rows.Count - existing,
            existing,
            parsed.ErrorRows,
            parsed.Rows.Select(x => CanonicalEquipmentName(x.Equipment)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray(),
            parsed.Issues);
    }

    public async Task<ExcelImportResult> ImportAsync(
        Stream content,
        Guid employeeId,
        bool overwriteExisting,
        CancellationToken cancellationToken = default)
    {
        var parsed = Parse(content);
        var equipmentByName = await dbContext.Equipment.ToDictionaryAsync(x => x.NormalizedName, cancellationToken);
        var existing = await dbContext.DieCuts.Include(x => x.Equipment).ToListAsync(cancellationToken);
        var existingByKey = existing.ToDictionary(x => Key(x.Equipment.Name, x.Number));
        var imported = 0;
        var updated = 0;
        var skipped = parsed.ErrorRows;

        foreach (var row in parsed.Rows)
        {
            var key = Key(row.Equipment, row.Number);
            if (existingByKey.TryGetValue(key, out var dieCut))
            {
                if (!overwriteExisting)
                {
                    skipped++;
                    continue;
                }
                Apply(dieCut, row, employeeId);
                updated++;
                continue;
            }

            var equipmentKey = NormalizeEquipment(row.Equipment);
            if (!equipmentByName.TryGetValue(equipmentKey, out var equipment))
            {
                equipment = new Equipment { Name = CanonicalEquipmentName(row.Equipment), NormalizedName = equipmentKey };
                dbContext.Equipment.Add(equipment);
                equipmentByName.Add(equipmentKey, equipment);
            }

            dieCut = new DieCut
            {
                Equipment = equipment,
                EquipmentId = equipment.Id,
                CreatedByEmployeeId = employeeId
            };
            Apply(dieCut, row, employeeId);
            dbContext.DieCuts.Add(dieCut);
            existingByKey.Add(key, dieCut);
            imported++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new ExcelImportResult(imported, updated, skipped, parsed.Issues);
    }

    private static ParsedWorkbook Parse(Stream content)
    {
        var rows = new List<ImportRow>();
        var issues = new List<ExcelImportIssue>();
        var seen = new HashSet<string>();
        var totalRows = 0;
        var errorRows = 0;

        try
        {
            using var document = SpreadsheetDocument.Open(content, false);
            var workbookPart = document.WorkbookPart ?? throw new InvalidDataException("В книге отсутствует рабочая область.");
            var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
            var workbook = workbookPart.Workbook ?? throw new InvalidDataException("В книге отсутствует рабочая область.");
            var sheets = workbook.Sheets ?? throw new InvalidDataException("В книге отсутствуют листы.");
            foreach (var sheet in sheets.Elements<Sheet>())
            {
                var sheetName = sheet.Name?.Value?.Trim() ?? "Лист";
                if (sheet.Id?.Value is null || workbookPart.GetPartById(sheet.Id.Value) is not WorksheetPart worksheetPart) continue;
                var worksheet = worksheetPart.Worksheet ?? throw new InvalidDataException($"Не удалось прочитать лист «{sheetName}».");
                var sheetRows = worksheet.GetFirstChild<SheetData>()?.Elements<Row>().ToList() ?? [];
                if (sheetRows.Count == 0) continue;
                if (!HeaderIsSupported(sheetRows[0], sharedStrings))
                {
                    issues.Add(new ExcelImportIssue(sheetName, 1, null, ImportIssueSeverity.Error,
                        "Ожидаются колонки №, shaft, X, Y, streams, repeats, x, y, material, H, figure и comments."));
                    errorRows++;
                    continue;
                }

                foreach (var sourceRow in sheetRows.Skip(1))
                {
                    var cells = ReadCells(sourceRow, sharedStrings);
                    if (cells.Values.All(string.IsNullOrWhiteSpace)) continue;
                    totalRows++;
                    var rowNumber = checked((int)(sourceRow.RowIndex?.Value ?? 0));
                    var number = Value(cells, 1)?.Trim();
                    try
                    {
                        if (string.IsNullOrWhiteSpace(number)) throw new InvalidDataException("Не указан номер ножа.");
                        var shaft = RequiredInt(cells, 2, "shaft");
                        var x = RequiredDecimal(cells, 3, "X");
                        var y = RequiredDecimal(cells, 4, "Y");
                        var streams = RequiredInt(cells, 5, "streams");
                        var repeats = RequiredInt(cells, 6, "repeats");
                        var h = RequiredDecimal(cells, 10, "H");
                        if (shaft <= 0 || x <= 0 || y <= 0 || streams <= 0 || repeats <= 0 || h <= 0)
                            throw new InvalidDataException("shaft, X (L), Y (B), streams (ручьи), repeats (этикеток в ручье) и H (ширина материала) должны быть больше нуля.");

                        var (gapX, gapY) = DieCutCalculations.Calculate(shaft, x, y, streams, repeats, h);
                        if (gapX < 0) throw new InvalidDataException("H (ширина материала) меньше X (L) × streams (ручьи).");
                        if (gapY < 0) throw new InvalidDataException("Длина окружности shaft не вмещает Y (B) × repeats (этикеток в ручье).");
                        CompareCalculatedValue(cells, 7, "x", gapX, sheetName, rowNumber, number, issues);
                        CompareCalculatedValue(cells, 8, "y", gapY, sheetName, rowNumber, number, issues);

                        var row = new ImportRow(
                            number,
                            sheetName,
                            shaft,
                            x,
                            y,
                            streams,
                            repeats,
                            gapX,
                            gapY,
                            RequiredText(cells, 9, "material"),
                            h,
                            NormalizeFigure(RequiredText(cells, 11, "figure")),
                            Value(cells, 12),
                            ParseDate(Value(cells, 13)));
                        var key = Key(row.Equipment, row.Number);
                        if (!seen.Add(key)) throw new InvalidDataException("Дублирующийся номер на том же листе/оборудовании.");
                        rows.Add(row);
                    }
                    catch (Exception exception) when (exception is InvalidDataException or FormatException or OverflowException)
                    {
                        errorRows++;
                        issues.Add(new ExcelImportIssue(sheetName, rowNumber, number, ImportIssueSeverity.Error, exception.Message));
                    }
                }
            }
        }
        catch (Exception exception) when (exception is DocumentFormat.OpenXml.Packaging.OpenXmlPackageException or InvalidDataException)
        {
            throw new InvalidDataException("Файл не является корректной книгой Excel (.xlsx).", exception);
        }

        return new ParsedWorkbook(rows, issues, totalRows, errorRows);
    }

    private static bool HeaderIsSupported(Row row, SharedStringTable? sharedStrings)
    {
        var cells = ReadCells(row, sharedStrings);
        return string.Equals(Value(cells, 1), "№", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Value(cells, 2), "shaft", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Value(cells, 3), "X", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Value(cells, 4), "Y", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Value(cells, 5), "streams", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Value(cells, 6), "repeats", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Value(cells, 7), "x", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Value(cells, 8), "y", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Value(cells, 9), "material", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Value(cells, 10), "H", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Value(cells, 11), "figure", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Value(cells, 12), "comments", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<int, string?> ReadCells(Row row, SharedStringTable? sharedStrings)
    {
        var result = new Dictionary<int, string?>();
        foreach (var cell in row.Elements<Cell>())
        {
            var index = ColumnIndex(cell.CellReference?.Value);
            var raw = cell.CellValue?.InnerText ?? cell.InlineString?.Text?.Text;
            if (cell.DataType?.Value == CellValues.SharedString && int.TryParse(raw, out var sharedIndex))
                raw = sharedStrings?.Elements<SharedStringItem>().ElementAtOrDefault(sharedIndex)?.InnerText;
            result[index] = raw;
        }
        return result;
    }

    private static int ColumnIndex(string? reference)
    {
        var index = 0;
        foreach (var character in reference ?? string.Empty)
        {
            if (!char.IsLetter(character)) break;
            index = index * 26 + char.ToUpperInvariant(character) - 'A' + 1;
        }
        return index;
    }

    private static string? Value(IReadOnlyDictionary<int, string?> cells, int column) =>
        cells.TryGetValue(column, out var value) ? value : null;

    private static string RequiredText(IReadOnlyDictionary<int, string?> cells, int column, string name) =>
        string.IsNullOrWhiteSpace(Value(cells, column))
            ? throw new InvalidDataException($"Не заполнена колонка {name}.")
            : Value(cells, column)!.Trim();

    private static decimal RequiredDecimal(IReadOnlyDictionary<int, string?> cells, int column, string name) =>
        ParseDecimal(RequiredText(cells, column, name), name);

    private static int RequiredInt(IReadOnlyDictionary<int, string?> cells, int column, string name)
    {
        var value = RequiredDecimal(cells, column, name);
        return value == decimal.Truncate(value) ? checked((int)value) : throw new InvalidDataException($"Колонка {name} должна содержать целое число.");
    }

    private static void CompareCalculatedValue(
        IReadOnlyDictionary<int, string?> cells,
        int column,
        string name,
        decimal calculated,
        string sheet,
        int row,
        string number,
        ICollection<ExcelImportIssue> issues)
    {
        var source = Value(cells, column);
        if (string.IsNullOrWhiteSpace(source)) return;
        var saved = ParseDecimal(source, name);
        if (Math.Abs(saved - calculated) <= 0.0000005m) return;
        issues.Add(new ExcelImportIssue(sheet, row, number, ImportIssueSeverity.Warning,
            $"Колонка {name} пересчитана по формуле Excel: {calculated:0.#########} вместо {saved:0.#########}."));
    }
    private static decimal ParseDecimal(string value, string name) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : decimal.TryParse(value, NumberStyles.Number, CultureInfo.GetCultureInfo("ru-RU"), out result)
                ? result
                : throw new InvalidDataException($"Колонка {name} содержит некорректное число «{value}».");

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial))
            return DateOnly.FromDateTime(DateTime.FromOADate(serial));
        if (DateTime.TryParse(value, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out var date))
            return DateOnly.FromDateTime(date);
        throw new InvalidDataException($"Некорректная дата «{value}».");
    }

    private static string NormalizeFigure(string value) => value.Trim().ToLowerInvariant() switch
    {
        "фигура" or "фигурный" => "фигурный",
        var shape => shape
    };

    private static void Apply(DieCut target, ImportRow source, Guid employeeId)
    {
        target.Number = source.Number.Trim();
        target.NormalizedNumber = Normalize(source.Number);
        target.Shaft = source.Shaft;
        target.X = source.X;
        target.Y = source.Y;
        target.Streams = source.Streams;
        target.Repeats = source.Repeats;
        target.GapX = source.GapX;
        target.GapY = source.GapY;
        target.Material = NormalizeMaterial(source.Material);
        target.H = source.H;
        target.Figure = source.Figure;
        target.Comments = string.IsNullOrWhiteSpace(source.Comments) ? null : source.Comments.Trim();
        target.Date = source.Date;
        target.UpdatedByEmployeeId = employeeId;
        target.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string NormalizeMaterial(string value) => value.Trim().ToLowerInvariant() switch
    {
        "paper" => "Paper",
        "ttop" => "TTOP",
        var _ => value.Trim()
    };

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private static string CanonicalEquipmentName(string value) => Normalize(value) switch
    {
        "NILPETER" or "NILPETER/LESKO" => "Nilpeter/Lesko",
        _ => value.Trim()
    };

    private static string NormalizeEquipment(string value) => Normalize(CanonicalEquipmentName(value));
    private static string Key(string equipment, string number) => NormalizeEquipment(equipment) + "\n" + Normalize(number);

    private sealed record ImportRow(
        string Number, string Equipment, int Shaft, decimal X, decimal Y,
        int Streams, int Repeats, decimal GapX, decimal GapY, string Material,
        decimal H, string Figure, string? Comments, DateOnly? Date);

    private sealed record ParsedWorkbook(
        IReadOnlyList<ImportRow> Rows,
        IReadOnlyList<ExcelImportIssue> Issues,
        int TotalRows,
        int ErrorRows);
}
