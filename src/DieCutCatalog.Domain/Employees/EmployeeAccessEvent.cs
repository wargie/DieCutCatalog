namespace DieCutCatalog.Domain.Employees;

public sealed class EmployeeAccessEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }
    public EmployeeAccessEventType Type { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    public Employee Employee { get; set; } = null!;
}