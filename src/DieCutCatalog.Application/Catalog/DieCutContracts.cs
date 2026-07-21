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
    string Equipment,
    int Shaft,
    decimal X,
    decimal Y,
    int Streams,
    int Repeats,
    string Material,
    decimal H,
    string Figure,
    string? Comments,
    DateOnly? Date,
    DieCutStatus Status);

public sealed record DieCutSummary(
    Guid Id,
    string Number,
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
    DieCutStatus Status,
    DateTimeOffset UpdatedAt);

public sealed record DieCutDetails(
    Guid Id,
    string Number,
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
    DieCutStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

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
    Task<CatalogFacets> GetFacetsAsync(CancellationToken cancellationToken = default);
}
