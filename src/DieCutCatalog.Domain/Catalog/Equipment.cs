namespace DieCutCatalog.Domain.Catalog;

public sealed class Equipment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public ICollection<DieCut> DieCuts { get; set; } = new List<DieCut>();
}
