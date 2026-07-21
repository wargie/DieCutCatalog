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

public interface IDieCutCatalogService
{
    Task<PagedResult<DieCutSummary>> SearchAsync(DieCutQuery query, CancellationToken cancellationToken = default);
    Task<DieCutDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DieCutDetails> CreateAsync(SaveDieCutCommand command, Guid employeeId, CancellationToken cancellationToken = default);
    Task<DieCutDetails?> UpdateAsync(Guid id, SaveDieCutCommand command, Guid employeeId, CancellationToken cancellationToken = default);
    Task<DieCutDetails?> AddCirculationAsync(Guid id, long quantity, Guid employeeId, CancellationToken cancellationToken = default);
    Task<DieCutDetails?> ResetMileageAsync(Guid id, Guid employeeId, CancellationToken cancellationToken = default);
    Task<DieCutDetails?> RetireAsync(Guid id, Guid employeeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DieCutEventDetails>?> GetEventsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CatalogFacets> GetFacetsAsync(CancellationToken cancellationToken = default);
}