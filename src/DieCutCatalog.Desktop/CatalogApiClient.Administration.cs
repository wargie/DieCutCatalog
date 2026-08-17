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

    public Task<ReferenceImportResult> ImportCatalogReferencesAsync(CatalogReferenceType type, IReadOnlyList<string> names) =>
        SendAsync<ReferenceImportResult>(HttpMethod.Post,
            $"api/catalog-administration/references/{type}/import", new { names });

    public Task<CatalogReferenceItem> RenameCatalogReferenceAsync(CatalogReferenceType type, Guid id, string name) =>
        SendAsync<CatalogReferenceItem>(HttpMethod.Put,
            $"api/catalog-administration/references/{type}/{id}", new { name });

    public Task UpdateCatalogReferenceArticleAsync(CatalogReferenceType type, Guid id, string? articleRtf) =>
        SendAsync(HttpMethod.Put, $"api/catalog-administration/references/{type}/{id}/article", new { articleRtf });

    public Task DeleteCatalogReferenceAsync(CatalogReferenceType type, Guid id, string password) =>
        SendAsync(HttpMethod.Delete, $"api/catalog-administration/references/{type}/{id}", new { password });

    public Task<ReferenceDirectoryOverview> GetReferenceDirectoryOverviewAsync() =>
        SendAsync<ReferenceDirectoryOverview>(HttpMethod.Get, "api/catalog-administration/directories");

    public Task<ReferenceDirectoryGroupItem> AddReferenceDirectoryGroupAsync(string name) =>
        SendAsync<ReferenceDirectoryGroupItem>(HttpMethod.Post, "api/catalog-administration/directory-groups", new { name });

    public Task DeleteReferenceDirectoryGroupAsync(Guid id, string password) =>
        SendAsync(HttpMethod.Delete, $"api/catalog-administration/directory-groups/{id}", new { password });

    public Task<ReferenceDirectoryItem> AddReferenceDirectoryAsync(Guid? groupId, string name, string? description) =>
        SendAsync<ReferenceDirectoryItem>(HttpMethod.Post, "api/catalog-administration/directories", new { groupId, name, description });

    public Task<ReferenceDirectoryItem> UpdateReferenceDirectoryAsync(
        Guid id, Guid? groupId, string name, string? description, bool isArchived) =>
        SendAsync<ReferenceDirectoryItem>(HttpMethod.Put, $"api/catalog-administration/directories/{id}",
            new { groupId, name, description, isArchived });

    public Task DeleteReferenceDirectoryAsync(Guid id) =>
        SendAsync(HttpMethod.Delete, $"api/catalog-administration/directories/{id}");

    public Task<IReadOnlyList<ReferenceDirectoryValueItem>> GetReferenceDirectoryValuesAsync(Guid id, bool includeArchived = true) =>
        SendAsync<IReadOnlyList<ReferenceDirectoryValueItem>>(HttpMethod.Get,
            $"api/catalog-administration/directories/{id}/values?includeArchived={includeArchived.ToString().ToLowerInvariant()}");

    public Task<ReferenceDirectoryValueItem> AddReferenceDirectoryValueAsync(Guid id, string name) =>
        SendAsync<ReferenceDirectoryValueItem>(HttpMethod.Post,
            $"api/catalog-administration/directories/{id}/values", new { name });

    public Task<ReferenceImportResult> ImportReferenceDirectoryValuesAsync(Guid id, IReadOnlyList<string> names) =>
        SendAsync<ReferenceImportResult>(HttpMethod.Post,
            $"api/catalog-administration/directories/{id}/values/import", new { names });

    public Task<ReferenceDirectoryValueItem> UpdateReferenceDirectoryValueAsync(
        Guid directoryId, Guid id, string name, bool isArchived) =>
        SendAsync<ReferenceDirectoryValueItem>(HttpMethod.Put,
            $"api/catalog-administration/directories/{directoryId}/values/{id}", new { name, isArchived });

    public Task DeleteReferenceDirectoryValueAsync(Guid directoryId, Guid id) =>
        SendAsync(HttpMethod.Delete, $"api/catalog-administration/directories/{directoryId}/values/{id}");

    public Task UpdateReferenceDirectoryValueArticleAsync(Guid directoryId, Guid id, string? articleRtf) =>
        SendAsync(HttpMethod.Put,
            $"api/catalog-administration/directories/{directoryId}/values/{id}/article", new { articleRtf });

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
