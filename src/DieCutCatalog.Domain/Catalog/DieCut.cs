namespace DieCutCatalog.Domain.Catalog;

public sealed class DieCut
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = string.Empty;
    public string NormalizedNumber { get; set; } = string.Empty;
    public Guid EquipmentId { get; set; }
    public Equipment Equipment { get; set; } = null!;

    public decimal ShaftRepeatMm { get; set; }
    public decimal WidthMm { get; set; }
    public decimal LengthMm { get; set; }
    public int Streams { get; set; }
    public int Repeats { get; set; }
    public decimal GapAcrossMm { get; set; }
    public decimal GapAlongMm { get; set; }
    public string Material { get; set; } = string.Empty;
    public decimal MaterialWidthMm { get; set; }
    public decimal? KnifeHeightMicrons { get; set; }
    public string Shape { get; set; } = string.Empty;
    public string? Comments { get; set; }
    public DateOnly? CommissionedOn { get; set; }
    public DieCutStatus Status { get; set; } = DieCutStatus.Active;

    public Guid CreatedByEmployeeId { get; set; }
    public Guid UpdatedByEmployeeId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
