namespace DieCutCatalog.Domain.Catalog;

public sealed class DieCut
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = string.Empty;
    public string NormalizedNumber { get; set; } = string.Empty;
    public string? JcOrderNumber { get; set; }
    public Guid EquipmentId { get; set; }
    public Equipment Equipment { get; set; } = null!;

    public int Shaft { get; set; }
    public decimal X { get; set; }
    public decimal Y { get; set; }
    public int Streams { get; set; }
    public int Repeats { get; set; }
    public decimal GrooveSpacing { get; set; }
    public decimal LabelCornerRadius { get; set; }
    public decimal GapX { get; set; }
    public decimal GapY { get; set; }
    public string Material { get; set; } = string.Empty;
    public decimal H { get; set; }
    public string Figure { get; set; } = string.Empty;
    public string? Comments { get; set; }
    public DateOnly? Date { get; set; }
    public long Mileage { get; set; }
    public decimal RunLengthMeters { get; set; }
    public long Revolutions { get; set; }
    public long LifetimeMileage { get; set; }
    public decimal LifetimeRunLengthMeters { get; set; }
    public long LifetimeRevolutions { get; set; }
    public int Generation { get; set; } = 1;
    public long NextInspectionRevolutions { get; set; } = 1_000_000;
    public DieCutStatus Status { get; set; } = DieCutStatus.Active;

    public Guid CreatedByEmployeeId { get; set; }
    public Guid UpdatedByEmployeeId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<DieCutEvent> Events { get; set; } = new List<DieCutEvent>();
    public ICollection<DieCutDocument> Documents { get; set; } = new List<DieCutDocument>();
}