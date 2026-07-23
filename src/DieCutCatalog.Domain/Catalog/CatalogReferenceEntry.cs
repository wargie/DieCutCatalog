namespace DieCutCatalog.Domain.Catalog;

public enum CatalogReferenceKind
{
    Material = 0,
    Figure = 1
}

public sealed class CatalogReferenceEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public CatalogReferenceKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
