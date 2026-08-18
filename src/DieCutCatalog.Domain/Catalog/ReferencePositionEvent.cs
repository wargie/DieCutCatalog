using DieCutCatalog.Domain.Employees;

namespace DieCutCatalog.Domain.Catalog;

public sealed class ReferencePositionEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }
    public ReferencePositionEventType Type { get; set; }
    public Guid SourcePositionId { get; set; }
    public Guid DestinationPositionId { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public string DestinationName { get; set; } = string.Empty;
    public string SourceSection { get; set; } = string.Empty;
    public string DestinationSection { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    public Employee Employee { get; set; } = null!;
}
