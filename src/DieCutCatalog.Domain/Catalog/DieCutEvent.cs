using DieCutCatalog.Domain.Employees;

namespace DieCutCatalog.Domain.Catalog;

public sealed class DieCutEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DieCutId { get; set; }
    public DieCut DieCut { get; set; } = null!;
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public DieCutEventType Type { get; set; }
    public long? Quantity { get; set; }
    public long MileageBefore { get; set; }
    public long MileageAfter { get; set; }
    public decimal RunLengthMetersBefore { get; set; }
    public decimal RunLengthMetersAfter { get; set; }
    public long RevolutionsBefore { get; set; }
    public long RevolutionsAfter { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}