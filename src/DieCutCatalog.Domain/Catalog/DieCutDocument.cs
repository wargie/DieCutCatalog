namespace DieCutCatalog.Domain.Catalog;

public sealed class DieCutDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DieCutId { get; set; }
    public DieCut DieCut { get; set; } = null!;
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/pdf";
    public long Size { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public DieCutDocumentSource Source { get; set; }
    public Guid CreatedByEmployeeId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}