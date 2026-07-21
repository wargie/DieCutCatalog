using DieCutCatalog.Domain.Catalog;

namespace DieCutCatalog.Application.Catalog;

public sealed record DieCutQuery(
    string? Search,
    string? Equipment,
    string? Material,
    string? Shape,
    DieCutStatus? Status,
    decimal? MinWidthMm,
    decimal? MaxWidthMm,
    decimal? MinLengthMm,
    decimal? MaxLengthMm,
    decimal? ShaftRepeatMm,
    int Page = 1,
    int PageSize = 50);

public sealed record SaveDieCutCommand(
    string Number,
    string Equipment,
    decimal ShaftRepeatMm,
    decimal WidthMm,
    decimal LengthMm,
    int Streams,
    int Repeats,
    decimal GapAcrossMm,
    decimal GapAlongMm,
    string Material,
    decimal MaterialWidthMm,
    decimal? KnifeHeightMicrons,
    string Shape,
    string? Comments,
    DateOnly? CommissionedOn,
    DieCutStatus Status);

public sealed record DieCutSummary(
    Guid Id,
    string Number,
    string Equipment,
    decimal ShaftRepeatMm,
    decimal WidthMm,
    decimal LengthMm,
    int Streams,
    int Repeats,
    string Material,
    decimal MaterialWidthMm,
    decimal? KnifeHeightMicrons,
    string Shape,
    DieCutStatus Status,
    DateTimeOffset UpdatedAt);

public sealed record DieCutDetails(
    Guid Id,
    string Number,
    string Equipment,
    decimal ShaftRepeatMm,
    decimal WidthMm,
    decimal LengthMm,
    int Streams,
    int Repeats,
    decimal GapAcrossMm,
    decimal GapAlongMm,
    string Material,
    decimal MaterialWidthMm,
    decimal? KnifeHeightMicrons,
    string Shape,
    string? Comments,
    DateOnly? CommissionedOn,
    DieCutStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record CatalogFacets(
    IReadOnlyList<string> Equipment,
    IReadOnlyList<string> Materials,
    IReadOnlyList<string> Shapes);

public interface IDieCutCatalogService
{
    Task<PagedResult<DieCutSummary>> SearchAsync(DieCutQuery query, CancellationToken cancellationToken = default);
    Task<DieCutDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DieCutDetails> CreateAsync(SaveDieCutCommand command, Guid employeeId, CancellationToken cancellationToken = default);
    Task<DieCutDetails?> UpdateAsync(Guid id, SaveDieCutCommand command, Guid employeeId, CancellationToken cancellationToken = default);
    Task<CatalogFacets> GetFacetsAsync(CancellationToken cancellationToken = default);
}
