using DieCutCatalog.Domain.Catalog;

namespace DieCutCatalog.Application.Catalog;

public sealed record DieCutQuery(
    string? Search,
    string? Equipment,
    string? Material,
    string? Figure,
    DieCutStatus? Status,
    decimal? MinX,
    decimal? MaxX,
    decimal? MinY,
    decimal? MaxY,
    int? Shaft,
    int Page = 1,
    int PageSize = 50);

public sealed record SaveDieCutCommand(
    string Number,
    string? JcOrderNumber,
    string Equipment,
    int Shaft,
    decimal X,
    decimal Y,
    int Streams,
    int Repeats,
    decimal GrooveSpacing,
    decimal LabelCornerRadius,
    string Material,
    decimal H,
    string Figure,
    string? Comments,
    DateOnly? Date,
    DieCutStatus Status);

public sealed record DieCutSummary(
    Guid Id,
    string Number,
    string? JcOrderNumber,
    string Equipment,
    int Shaft,
    decimal X,
    decimal Y,
    int Streams,
    int Repeats,
    decimal GapX,
    decimal GapY,
    string Material,
    decimal H,
    string Figure,
    string? Comments,
    DateOnly? Date,
    long Mileage,
    decimal RunLengthMeters,
    long Revolutions,
    long LifetimeMileage,
    decimal LifetimeRunLengthMeters,
    long LifetimeRevolutions,
    int Generation,
    long NextInspectionRevolutions,
    DieCutStatus Status,
    DateTimeOffset UpdatedAt);

public sealed record DieCutDetails(
    Guid Id,
    string Number,
    string? JcOrderNumber,
    string Equipment,
    int Shaft,
    decimal X,
    decimal Y,
    int Streams,
    int Repeats,
    decimal GrooveSpacing,
    decimal LabelCornerRadius,
    decimal GapX,
    decimal GapY,
    string Material,
    decimal H,
    string Figure,
    string? Comments,
    DateOnly? Date,
    long Mileage,
    decimal RunLengthMeters,
    long Revolutions,
    long LifetimeMileage,
    decimal LifetimeRunLengthMeters,
    long LifetimeRevolutions,
    int Generation,
    long NextInspectionRevolutions,
    DieCutStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record DieCutEventDetails(
    Guid Id,
    DieCutEventType Type,
    long? Quantity,
    long MileageBefore,
    long MileageAfter,
    decimal RunLengthMetersBefore,
    decimal RunLengthMetersAfter,
    long RevolutionsBefore,
    long RevolutionsAfter,
    DateTimeOffset OccurredAt,
    Guid EmployeeId,
    string EmployeeName);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record CatalogFacets(
    IReadOnlyList<string> Equipment,
    IReadOnlyList<string> Materials,
    IReadOnlyList<string> Figures);

public enum CatalogReferenceType
{
    Material,
    Figure,
    Equipment
}

public sealed record CatalogReferenceItem(Guid Id, CatalogReferenceType Type, string Name);
public sealed record CatalogReferences(
    IReadOnlyList<CatalogReferenceItem> Materials,
    IReadOnlyList<CatalogReferenceItem> Figures,
    IReadOnlyList<CatalogReferenceItem> Equipment);

public sealed record AuditLogEntry(
    Guid Id,
    Guid DieCutId,
    string DieCutNumber,
    string Equipment,
    DieCutEventType Type,
    long? Quantity,
    long MileageBefore,
    long MileageAfter,
    decimal RunLengthMetersBefore,
    decimal RunLengthMetersAfter,
    long RevolutionsBefore,
    long RevolutionsAfter,
    DateTimeOffset OccurredAt,
    string EmployeeName);

public sealed record ExportedFile(string FileName, string ContentType, byte[] Content);

public interface ICatalogAdministrationService
{
    Task<CatalogReferences> GetReferencesAsync(CancellationToken cancellationToken = default);
    Task<CatalogReferenceItem> AddReferenceAsync(CatalogReferenceType type, string name, CancellationToken cancellationToken = default);
    Task<CatalogReferenceItem?> RenameReferenceAsync(CatalogReferenceType type, Guid id, string name, CancellationToken cancellationToken = default);
    Task<bool> DeleteReferenceAsync(CatalogReferenceType type, Guid id, CancellationToken cancellationToken = default);
    Task<PagedResult<AuditLogEntry>> SearchAuditLogAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ExportedFile> ExportAuditLogAsync(string? search, bool pdf, CancellationToken cancellationToken = default);
}

public sealed record PdfImportPreview(
    string? Number,
    int? Shaft,
    decimal? LabelWidth,
    decimal? LabelLength,
    int? Streams,
    int? Repeats,
    decimal? GrooveSpacing,
    decimal? LabelCornerRadius,
    string? Material,
    decimal? MaterialWidth,
    IReadOnlyList<string> Warnings);

public sealed record DieCutDocumentDetails(
    Guid Id,
    string FileName,
    DieCutDocumentSource Source,
    long Size,
    string Sha256,
    DateTimeOffset CreatedAt);

public sealed record StoredPdf(string FileName, string ContentType, Stream Content);

public interface IDieCutPdfService
{
    Task<PdfImportPreview> PreviewAsync(Stream content, long size, CancellationToken cancellationToken = default);
    Task<DieCutDocumentDetails?> UploadAsync(Guid dieCutId, string fileName, Stream content, long size, Guid employeeId, CancellationToken cancellationToken = default);
    Task<DieCutDocumentDetails?> GenerateAsync(Guid dieCutId, Guid employeeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DieCutDocumentDetails>?> ListAsync(Guid dieCutId, CancellationToken cancellationToken = default);
    Task<StoredPdf?> OpenAsync(Guid dieCutId, Guid documentId, CancellationToken cancellationToken = default);
}

public interface IDieCutCatalogService
{
    Task<PagedResult<DieCutSummary>> SearchAsync(DieCutQuery query, CancellationToken cancellationToken = default);
    Task<DieCutDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DieCutDetails> CreateAsync(SaveDieCutCommand command, Guid employeeId, CancellationToken cancellationToken = default);
    Task<DieCutDetails?> UpdateAsync(Guid id, SaveDieCutCommand command, Guid employeeId, CancellationToken cancellationToken = default);
    Task<DieCutDetails?> AddCirculationAsync(Guid id, long quantity, Guid employeeId, CancellationToken cancellationToken = default);
    Task<DieCutDetails?> InstallReplacementAsync(Guid id, Guid employeeId, CancellationToken cancellationToken = default);
    Task<DieCutDetails?> RetireAsync(Guid id, Guid employeeId, CancellationToken cancellationToken = default);
    Task<DieCutDetails?> DeleteAsync(Guid id, Guid employeeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DieCutEventDetails>?> GetEventsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CatalogFacets> GetFacetsAsync(CancellationToken cancellationToken = default);
}