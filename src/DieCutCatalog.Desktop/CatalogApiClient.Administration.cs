using System.Net.Http;
using System.Text;
using DieCutCatalog.Application.Catalog;

namespace DieCutCatalog.Desktop;

internal sealed partial class CatalogApiClient
{
    public Task<CatalogReferences> GetCatalogReferencesAsync() =>
        SendAsync<CatalogReferences>(HttpMethod.Get, "api/catalog-administration/references");

    public Task<CatalogReferenceItem> AddCatalogReferenceAsync(CatalogReferenceType type, string name) =>
        SendAsync<CatalogReferenceItem>(HttpMethod.Post,
            $"api/catalog-administration/references/{type}", new { name });

    public Task<CatalogReferenceItem> RenameCatalogReferenceAsync(CatalogReferenceType type, Guid id, string name) =>
        SendAsync<CatalogReferenceItem>(HttpMethod.Put,
            $"api/catalog-administration/references/{type}/{id}", new { name });

    public Task DeleteCatalogReferenceAsync(CatalogReferenceType type, Guid id, string password) =>
        SendAsync(HttpMethod.Delete, $"api/catalog-administration/references/{type}/{id}", new { password });

    public Task<PagedResult<AuditLogEntry>> SearchAuditLogAsync(string? search, int page, int pageSize = 200)
    {
        var query = new StringBuilder($"api/catalog-administration/audit-log?page={page}&pageSize={pageSize}");
        Append(query, "search", search);
        return SendAsync<PagedResult<AuditLogEntry>>(HttpMethod.Get, query.ToString());
    }

    public async Task<byte[]> ExportAuditLogAsync(string? search, string format)
    {
        var query = new StringBuilder($"api/catalog-administration/audit-log/export?format={Uri.EscapeDataString(format)}");
        Append(query, "search", search);
        using var request = CreateRequest(HttpMethod.Get, query.ToString());
        using var response = await SendCoreAsync(request);
        return await response.Content.ReadAsByteArrayAsync();
    }
}
