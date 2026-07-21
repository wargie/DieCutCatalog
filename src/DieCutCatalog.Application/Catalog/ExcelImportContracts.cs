namespace DieCutCatalog.Application.Catalog;

public enum ImportIssueSeverity
{
    Warning = 0,
    Error = 1
}

public sealed record ExcelImportIssue(
    string Sheet,
    int Row,
    string? Number,
    ImportIssueSeverity Severity,
    string Message);

public sealed record ExcelImportPreview(
    int TotalRows,
    int ValidRows,
    int NewRows,
    int ExistingRows,
    int ErrorRows,
    IReadOnlyList<string> Equipment,
    IReadOnlyList<ExcelImportIssue> Issues);

public sealed record ExcelImportResult(
    int ImportedRows,
    int UpdatedRows,
    int SkippedRows,
    IReadOnlyList<ExcelImportIssue> Issues);

public interface IExcelCatalogImportService
{
    Task<ExcelImportPreview> PreviewAsync(Stream content, CancellationToken cancellationToken = default);
    Task<ExcelImportResult> ImportAsync(Stream content, Guid employeeId, bool overwriteExisting, CancellationToken cancellationToken = default);
}
