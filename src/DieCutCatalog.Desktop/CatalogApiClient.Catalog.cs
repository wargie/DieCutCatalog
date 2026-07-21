using System.IO;
using System.Net.Http;
using System.Text;
using DieCutCatalog.Application.Catalog;

namespace DieCutCatalog.Desktop;

internal sealed partial class CatalogApiClient
{
    public Task<PagedResult<DieCutSummary>> SearchDieCutsAsync(
        string? search,
        string? equipment,
        string? material,
        string? figure,
        int page,
        int pageSize = 100)
    {
        var query = new StringBuilder($"api/die-cuts/?page={page}&pageSize={pageSize}");
        Append(query, "search", search);
        Append(query, "equipment", equipment);
        Append(query, "material", material);
        Append(query, "figure", figure);
        return SendAsync<PagedResult<DieCutSummary>>(HttpMethod.Get, query.ToString());
    }

    public Task<CatalogFacets> GetCatalogFacetsAsync() =>
        SendAsync<CatalogFacets>(HttpMethod.Get, "api/die-cuts/facets");

    public Task<DieCutDetails> GetDieCutAsync(Guid id) =>
        SendAsync<DieCutDetails>(HttpMethod.Get, $"api/die-cuts/{id}");

    public Task<DieCutDetails> CreateDieCutAsync(SaveDieCutCommand command) =>
        SendAsync<DieCutDetails>(HttpMethod.Post, "api/die-cuts/", command);

    public Task<DieCutDetails> UpdateDieCutAsync(Guid id, SaveDieCutCommand command) =>
        SendAsync<DieCutDetails>(HttpMethod.Put, $"api/die-cuts/{id}", command);

    public Task<ExcelImportPreview> PreviewExcelImportAsync(string filePath) =>
        SendExcelFileAsync<ExcelImportPreview>("api/catalog-import/excel/preview", filePath);

    public Task<ExcelImportResult> CommitExcelImportAsync(string filePath, bool overwriteExisting) =>
        SendExcelFileAsync<ExcelImportResult>($"api/catalog-import/excel/commit?overwriteExisting={overwriteExisting.ToString().ToLowerInvariant()}", filePath);

    private async Task<T> SendExcelFileAsync<T>(string path, string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        using var form = new MultipartFormDataContent();
        using var file = new StreamContent(stream);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(file, "file", Path.GetFileName(filePath));
        return await SendAsync<T>(HttpMethod.Post, path, form);
    }

    private static void Append(StringBuilder query, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            query.Append('&').Append(name).Append('=').Append(Uri.EscapeDataString(value.Trim()));
        }
    }
}
