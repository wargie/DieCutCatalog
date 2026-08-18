using DieCutCatalog.Domain.Catalog;
using DieCutCatalog.Domain.Auditing;
using DieCutCatalog.Domain.Employees;

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
    int PageSize = 50,
    DieCutSortField SortBy = DieCutSortField.Default,
    bool SortDescending = false);

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
    DieCutStatus Status,
    JustCutPriceResult? JustCutPrice = null);

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
    decimal? JustCutPriceAmount,
    string? JustCutPriceCurrency,
    bool? JustCutPriceIncludesVat,
    long? JustCutNumberOrder,
    DateTimeOffset? JustCutCalculatedAt,
    string? JustCutEnvironment,
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
    decimal? JustCutPriceAmount,
    string? JustCutPriceCurrency,
    bool? JustCutPriceIncludesVat,
    long? JustCutNumberOrder,
    DateTimeOffset? JustCutCalculatedAt,
    string? JustCutEnvironment,
    DateTimeOffset OccurredAt,
    Guid EmployeeId,
    string EmployeeName);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record CatalogFacets(
    IReadOnlyList<string> Equipment,
    IReadOnlyList<string> Materials,
    IReadOnlyList<string> Figures,
    IReadOnlyList<decimal> LabelWidths,
    IReadOnlyList<decimal> LabelLengths,
    IReadOnlyList<int> Shafts);

public enum DieCutSortField
{
    Default,
    LabelWidth,
    LabelLength
}

public enum CatalogReferenceType
{
    Material,
    Figure,
    Equipment
}

public sealed record CatalogReferenceItem(Guid Id, CatalogReferenceType Type, string Name, string? ArticleRtf = null);
public sealed record CatalogReferences(
    IReadOnlyList<CatalogReferenceItem> Materials,
    IReadOnlyList<CatalogReferenceItem> Figures,
    IReadOnlyList<CatalogReferenceItem> Equipment);
public sealed record ReferenceImportCommand(IReadOnlyList<string> Names);
public sealed record ReferenceImportResult(int Added, int Skipped);

public sealed record ReferenceDirectoryGroupItem(Guid Id, string Name, int SortOrder);
public sealed record ReferenceDirectoryItem(
    Guid Id, Guid? GroupId, string Name, string? Description, int SortOrder, bool IsArchived, int ValueCount);
public sealed record ReferenceDirectoryValueItem(
    Guid Id, Guid DirectoryId, string Name, int SortOrder, bool IsArchived, DateTimeOffset UpdatedAt,
    string? ArticleRtf = null);
public sealed record ReferenceArticleCommand(string? ArticleRtf);
public sealed record AuditIdentity(
    Guid ActorEmployeeId,
    Guid? ApproverEmployeeId = null,
    Guid? CorrelationId = null);
public sealed record ReferencePositionLocator(CatalogReferenceType? SystemType, Guid? DirectoryId, Guid Id);
public sealed record ReferencePositionTarget(CatalogReferenceType? SystemType, Guid? DirectoryId);
public sealed record ReferencePositionTransferCommand(
    ReferencePositionLocator Source,
    ReferencePositionTarget Destination,
    string Name,
    bool Move,
    AuditIdentity Audit);
public sealed record ReferencePositionTransferResult(
    Guid Id,
    string Name,
    string? ArticleRtf,
    bool IsArchived);
public sealed record ReferenceDirectoryOverview(
    IReadOnlyList<ReferenceDirectoryGroupItem> Groups,
    IReadOnlyList<ReferenceDirectoryItem> Directories);
public sealed record CreateReferenceDirectoryCommand(Guid? GroupId, string Name, string? Description);
public sealed record UpdateReferenceDirectoryCommand(Guid? GroupId, string Name, string? Description, bool IsArchived);

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
    string EmployeeName,
    EmployeeAccessEventType? AccessType = null,
    AuditEntityType? EntityType = null,
    AuditAction? AuditAction = null,
    Guid? EntityId = null,
    Guid? ApproverEmployeeId = null,
    string? ApproverName = null,
    string? BeforeJson = null,
    string? AfterJson = null,
    Guid? CorrelationId = null,
    string? DisplayObject = null,
    string? DisplayContext = null);

public sealed record ExportedFile(string FileName, string ContentType, byte[] Content);

public interface ICatalogAdministrationService
{
    Task<CatalogReferences> GetReferencesAsync(CancellationToken cancellationToken = default);
    Task<CatalogReferenceItem> AddReferenceAsync(CatalogReferenceType type, string name, AuditIdentity audit, CancellationToken cancellationToken = default);
    Task<ReferenceImportResult> ImportReferencesAsync(CatalogReferenceType type, IReadOnlyList<string> names, AuditIdentity audit, CancellationToken cancellationToken = default);
    Task<CatalogReferenceItem?> RenameReferenceAsync(CatalogReferenceType type, Guid id, string name, AuditIdentity audit, CancellationToken cancellationToken = default);
    Task<bool> UpdateReferenceArticleAsync(CatalogReferenceType type, Guid id, string? articleRtf, AuditIdentity audit, CancellationToken cancellationToken = default);
    Task<bool> DeleteReferenceAsync(CatalogReferenceType type, Guid id, AuditIdentity audit, CancellationToken cancellationToken = default);
    Task<ReferenceDirectoryOverview> GetDirectoryOverviewAsync(CancellationToken cancellationToken = default);
    Task<ReferenceDirectoryGroupItem> AddDirectoryGroupAsync(string name, AuditIdentity audit, CancellationToken cancellationToken = default);
    Task<bool> DeleteDirectoryGroupAsync(Guid id, AuditIdentity audit, CancellationToken cancellationToken = default);
    Task<ReferenceDirectoryItem> AddDirectoryAsync(CreateReferenceDirectoryCommand command, AuditIdentity audit, CancellationToken cancellationToken = default);
    Task<ReferenceDirectoryItem?> UpdateDirectoryAsync(Guid id, UpdateReferenceDirectoryCommand command, AuditIdentity audit, CancellationToken cancellationToken = default);
    Task<bool> DeleteDirectoryAsync(Guid id, AuditIdentity audit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReferenceDirectoryValueItem>> GetDirectoryValuesAsync(Guid directoryId, bool includeArchived, CancellationToken cancellationToken = default);
    Task<ReferenceDirectoryValueItem> AddDirectoryValueAsync(Guid directoryId, string name, AuditIdentity audit, CancellationToken cancellationToken = default);
    Task<ReferenceImportResult> ImportDirectoryValuesAsync(Guid directoryId, IReadOnlyList<string> names, AuditIdentity audit, CancellationToken cancellationToken = default);
    Task<ReferenceDirectoryValueItem?> UpdateDirectoryValueAsync(Guid directoryId, Guid id, string name, bool isArchived, AuditIdentity audit, CancellationToken cancellationToken = default);
    Task<bool> UpdateDirectoryValueArticleAsync(Guid directoryId, Guid id, string? articleRtf, AuditIdentity audit, CancellationToken cancellationToken = default);
    Task<bool> DeleteDirectoryValueAsync(Guid directoryId, Guid id, AuditIdentity audit, CancellationToken cancellationToken = default);
    Task<ReferencePositionTransferResult?> TransferPositionAsync(
        ReferencePositionTransferCommand command,
        CancellationToken cancellationToken = default);
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
    Task<DieCutDetails?> AddCirculationAsync(Guid id, long? quantity, decimal? runLengthMeters, Guid employeeId, CancellationToken cancellationToken = default);
    Task<DieCutDetails?> InstallReplacementAsync(Guid id, Guid employeeId, CancellationToken cancellationToken = default);
    Task<DieCutDetails?> RetireAsync(Guid id, Guid employeeId, CancellationToken cancellationToken = default);
    Task<DieCutDetails?> DeleteAsync(Guid id, Guid employeeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DieCutEventDetails>?> GetEventsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CatalogFacets> GetFacetsAsync(CancellationToken cancellationToken = default);
}
