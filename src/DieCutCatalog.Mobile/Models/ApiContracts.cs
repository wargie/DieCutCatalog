namespace DieCutCatalog.Mobile.Models;

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, bool MustChangePassword, EmployeeProfileDto Profile);

public sealed record EmployeeProfileDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? Position,
    string? Phone,
    string? AdditionalContacts,
    string? PhotoUrl,
    int Role,
    bool MustChangePassword,
    bool IsActive);

public sealed record PagedResultDto<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record DieCutDto(
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
    long Mileage,
    decimal RunLengthMeters,
    long Revolutions,
    int Status)
{
    public KnifeListItem ToListItem()
    {
        var (status, color) = Status switch
        {
            1 => ("Требует проверки", "#C42B1C"),
            2 => ("Списан", "#616161"),
            4 => ("Заказать новый", "#B25E09"),
            _ => ("OK", "#107C41")
        };

        return new KnifeListItem(Id, Number, status, color, Equipment, Material, X, Y, Shaft,
            Streams, Repeats, GapY, Mileage, RunLengthMeters, Revolutions, Figure);
    }
}
