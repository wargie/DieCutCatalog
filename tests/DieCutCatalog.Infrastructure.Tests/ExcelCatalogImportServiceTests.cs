using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DieCutCatalog.Domain.Catalog;
using DieCutCatalog.Application.Catalog;
using DieCutCatalog.Infrastructure.Catalog;
using DieCutCatalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DieCutCatalog.Infrastructure.Tests;

public sealed class ExcelCatalogImportServiceTests
{
    [Fact]
    public async Task Preview_ReportsValidAndInvalidRows()
    {
        await using var dbContext = CreateDbContext();
        var service = new ExcelCatalogImportService(dbContext);
        using var workbook = CreateWorkbook(includeInvalidRow: true);

        var preview = await service.PreviewAsync(workbook);

        Assert.Equal(2, preview.TotalRows);
        Assert.Equal(1, preview.ValidRows);
        Assert.Equal(1, preview.NewRows);
        Assert.Equal(1, preview.ErrorRows);
        Assert.Single(preview.Issues);
        Assert.Equal("Nilpeter/Lesko", preview.Equipment[0]);
    }

    [Fact]
    public async Task Import_RecalculatesExcelFormulasAndPreservesDate()
    {
        await using var dbContext = CreateDbContext();
        var service = new ExcelCatalogImportService(dbContext);
        using var workbook = CreateWorkbook(includeInvalidRow: false);

        var result = await service.ImportAsync(workbook, Guid.NewGuid(), overwriteExisting: false);
        var dieCut = await dbContext.DieCuts.Include(x => x.Equipment).SingleAsync();

        Assert.Equal(1, result.ImportedRows);
        Assert.Equal("001", dieCut.Number);
        Assert.Equal("Nilpeter/Lesko", dieCut.Equipment.Name);
        Assert.Equal(96, dieCut.Shaft);
        Assert.Equal(0.028m, dieCut.GapX);
        Assert.Equal(0.0048m, dieCut.GapY);
        Assert.Equal("Paper", dieCut.Material);
        Assert.Equal(200m, dieCut.H);
        Assert.Equal(DateOnly.FromDateTime(DateTime.FromOADate(46140)), dieCut.Date);
    }

    [Fact]
    public async Task Preview_RejectsFractionalShaft()
    {
        await using var dbContext = CreateDbContext();
        var service = new ExcelCatalogImportService(dbContext);
        using var workbook = CreateWorkbook(includeInvalidRow: false, shaft: 96.5m);

        var preview = await service.PreviewAsync(workbook);

        Assert.Equal(0, preview.ValidRows);
        Assert.Equal(1, preview.ErrorRows);
        Assert.Contains("целое число", Assert.Single(preview.Issues).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preview_RejectsProductionParametersAboveUpperLimits()
    {
        await using var dbContext = CreateDbContext();
        var service = new ExcelCatalogImportService(dbContext);
        using var workbook = CreateWorkbook(includeInvalidRow: false, streams: DieCutParameterLimits.MaximumStreams + 1);

        var preview = await service.PreviewAsync(workbook);

        Assert.Equal(0, preview.ValidRows);
        Assert.Equal(1, preview.ErrorRows);
        Assert.Contains(DieCutParameterLimits.MaximumStreams.ToString(), Assert.Single(preview.Issues).Message, StringComparison.Ordinal);
    }
    [Fact]
    public async Task Preview_WarnsWhenSavedFormulaValuesAreStale()
    {
        await using var dbContext = CreateDbContext();
        var service = new ExcelCatalogImportService(dbContext);
        using var workbook = CreateWorkbook(includeInvalidRow: false, savedGapX: 0.9m, savedGapY: 0.9m);

        var preview = await service.PreviewAsync(workbook);

        Assert.Equal(1, preview.ValidRows);
        Assert.Equal(0, preview.ErrorRows);
        Assert.Equal(2, preview.Issues.Count);
        Assert.All(preview.Issues, issue => Assert.Equal(ImportIssueSeverity.Warning, issue.Severity));
    }

    private static CatalogDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new CatalogDbContext(options);
    }

    private static MemoryStream CreateWorkbook(
        bool includeInvalidRow,
        decimal savedGapX = 0.028m,
        decimal savedGapY = 0.0048m,
        decimal shaft = 96,
        int streams = 2)
    {
        var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            worksheetPart.Worksheet = new Worksheet(sheetData);
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "NilPeter"
            });

            sheetData.Append(Row(1, "№", "shaft", "X", "Y", "streams", "repeats", "x", "y", "material", "H", "figure", "comments", "Дата"));
            sheetData.Append(Row(2, "001", shaft, 86, 300, streams, 1, savedGapX, savedGapY, "paper", 200, "прямоугольник", "старый", 46140));
            if (includeInvalidRow)
                sheetData.Append(Row(3, "002", 96, 58, 90, 7, 4, 0.029m, 0.0028m, null, 430, "прямоугольник", null, null));
            workbookPart.Workbook.Save();
        }
        stream.Position = 0;
        return stream;
    }

    private static Row Row(uint index, params object?[] values)
    {
        var row = new Row { RowIndex = index };
        for (var column = 0; column < values.Length; column++)
        {
            if (values[column] is null) continue;
            var cell = new Cell { CellReference = $"{ColumnName(column + 1)}{index}" };
            if (values[column] is string text)
            {
                cell.DataType = CellValues.String;
                cell.CellValue = new CellValue(text);
            }
            else
            {
                cell.DataType = CellValues.Number;
                cell.CellValue = new CellValue(Convert.ToString(values[column], System.Globalization.CultureInfo.InvariantCulture)!);
            }
            row.Append(cell);
        }
        return row;
    }

    private static string ColumnName(int index)
    {
        var name = string.Empty;
        while (index > 0)
        {
            index--;
            name = (char)('A' + index % 26) + name;
            index /= 26;
        }
        return name;
    }
}
