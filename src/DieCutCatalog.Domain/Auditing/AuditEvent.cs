using DieCutCatalog.Domain.Employees;

namespace DieCutCatalog.Domain.Auditing;

public sealed class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid ActorEmployeeId { get; set; }
    public Guid? ApproverEmployeeId { get; set; }
    public AuditEntityType EntityType { get; set; }
    public Guid EntityId { get; set; }
    public AuditAction Action { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public Guid CorrelationId { get; set; } = Guid.NewGuid();

    public Employee ActorEmployee { get; set; } = null!;
    public Employee? ApproverEmployee { get; set; }
}
