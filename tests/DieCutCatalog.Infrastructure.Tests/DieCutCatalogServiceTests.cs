using System.ComponentModel.DataAnnotations;
using DieCutCatalog.Application.Catalog;
using DieCutCatalog.Domain.Catalog;
using DieCutCatalog.Domain.Employees;
using DieCutCatalog.Infrastructure.Catalog;
using DieCutCatalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DieCutCatalog.Infrastructure.Tests;

public sealed class DieCutCatalogServiceTests
{
    [Fact]
    public async Task Create_RejectsDuplicateNumberForSameEquipment()
    {
        await using var fixture = CreateFixture();
        await fixture.Service.CreateAsync(NewDieCut("001", "Nilpeter/Lesko"), fixture.EmployeeId);

        await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.Service.CreateAsync(NewDieCut(" 001 ", "nilpeter/lesko"), fixture.EmployeeId));

        var otherEquipment = await fixture.Service.CreateAsync(NewDieCut("001", "Big Lesko"), fixture.EmployeeId);
        Assert.Equal("Big Lesko", otherEquipment.Equipment);
    }

    [Fact]
    public async Task Update_CanMoveLegacyKnifeWhenTargetNumberWasDeleted()
    {
        await using var fixture = CreateFixture();
        var deletedTarget = await fixture.Service.CreateAsync(NewDieCut("001", "Nilpeter/Lesko"), fixture.EmployeeId);
        await fixture.Service.DeleteAsync(deletedTarget.Id, fixture.EmployeeId);
        var legacy = await fixture.Service.CreateAsync(NewDieCut("001", "NilPeter"), fixture.EmployeeId);

        var moved = await fixture.Service.UpdateAsync(legacy.Id, NewDieCut("001", "Nilpeter/Lesko"), fixture.EmployeeId);

        Assert.NotNull(moved);
        Assert.Equal("Nilpeter/Lesko", moved.Equipment);
        Assert.Equal(legacy.Id, moved.Id);
    }

    [Fact]
    public async Task Create_WritesCreatedEvent()
    {
        await using var fixture = CreateFixture();
        var earliest = DateTimeOffset.UtcNow;

        var created = await fixture.Service.CreateAsync(NewDieCut("001", "Nilpeter/Lesko"), fixture.EmployeeId);
        var events = await fixture.Service.GetEventsAsync(created.Id);

        var createdEvent = Assert.Single(events!, item => item.Type == DieCutEventType.Created);
        Assert.Equal(fixture.EmployeeId, createdEvent.EmployeeId);
        Assert.InRange(createdEvent.OccurredAt, earliest, DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Create_CalculatesA1AndA2LikeExcel()
    {
        await using var fixture = CreateFixture();

        var created = await fixture.Service.CreateAsync(
            NewDieCut("010", "Big Lesko", x: 101), fixture.EmployeeId);

        Assert.Equal(0.026m, created.GapX);
        Assert.Equal(0.0022m, created.GapY);
    }

    [Fact]
    public async Task Create_PersistsDrawingParameters()
    {
        await using var fixture = CreateFixture();
        var command = NewDieCut("011", "Big Lesko") with
        {
            GrooveSpacing = 2.75m,
            LabelCornerRadius = 4.5m
        };

        var created = await fixture.Service.CreateAsync(command, fixture.EmployeeId);
        var loaded = await fixture.Service.GetAsync(created.Id);

        Assert.NotNull(loaded);
        Assert.Equal(2.75m, loaded.GrooveSpacing);
        Assert.Equal(4.5m, loaded.LabelCornerRadius);
    }

    [Fact]
    public async Task Create_RejectsInvalidDrawingParameters()
    {
        await using var fixture = CreateFixture();

        await Assert.ThrowsAsync<ValidationException>(() => fixture.Service.CreateAsync(
            NewDieCut("011", "Big Lesko") with { GrooveSpacing = -0.001m }, fixture.EmployeeId));
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Service.CreateAsync(
            NewDieCut("012", "Big Lesko") with { LabelCornerRadius = -0.001m }, fixture.EmployeeId));
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Service.CreateAsync(
            NewDieCut("013", "Big Lesko") with { LabelCornerRadius = 38m }, fixture.EmployeeId));
    }

    [Fact]
    public async Task Search_AppliesFiltersPaginationAndJcOrderSearch()
    {
        await using var fixture = CreateFixture();
        await fixture.Service.CreateAsync(NewDieCut("001", "Nilpeter/Lesko", material: "Paper", x: 58, jcOrderNumber: "JC-4201"), fixture.EmployeeId);
        await fixture.Service.CreateAsync(NewDieCut("002", "Nilpeter/Lesko", material: "TTOP", x: 80), fixture.EmployeeId);
        await fixture.Service.CreateAsync(NewDieCut("003", "Big Lesko", material: "Paper", x: 100), fixture.EmployeeId);

        var result = await fixture.Service.SearchAsync(new DieCutQuery(
            Search: "jc-4201",
            Equipment: "Nilpeter/Lesko",
            Material: "Paper",
            Figure: null,
            Status: DieCutStatus.Active,
            MinX: 50,
            MaxX: 70,
            MinY: null,
            MaxY: null,
            Shaft: null,
            Page: 1,
            PageSize: 1));

        Assert.Equal(1, result.Total);
        Assert.Single(result.Items);
        Assert.Equal("001", result.Items[0].Number);
        Assert.Equal("JC-4201", result.Items[0].JcOrderNumber);
    }

    [Fact]
    public async Task Update_ChangesCardAndCreatesEquipmentFacet()
    {
        await using var fixture = CreateFixture();
        var created = await fixture.Service.CreateAsync(NewDieCut("001", "Nilpeter/Lesko"), fixture.EmployeeId);
        var command = NewDieCut("001A", "MarkAndy", material: "PP60 TOP WHITE", x: 85, jcOrderNumber: "JC-88") with
        {
            Comments = "RLL",
            Status = DieCutStatus.NeedsInspection
        };

        var updated = await fixture.Service.UpdateAsync(created.Id, command, fixture.EmployeeId);
        var facets = await fixture.Service.GetFacetsAsync();

        Assert.NotNull(updated);
        Assert.Equal("001A", updated.Number);
        Assert.Equal("JC-88", updated.JcOrderNumber);
        Assert.Equal("MarkAndy", updated.Equipment);
        Assert.Equal(DieCutStatus.NeedsInspection, updated.Status);
        Assert.Contains("MarkAndy", facets.Equipment);
        Assert.Contains("PP60 TOP WHITE", facets.Materials);
    }

    [Fact]
    public async Task Update_AllowsOrderNewStatus()
    {
        await using var fixture = CreateFixture();
        var created = await fixture.Service.CreateAsync(
            NewDieCut("ORDER-001", "Nilpeter/Lesko"), fixture.EmployeeId);

        var updated = await fixture.Service.UpdateAsync(
            created.Id,
            NewDieCut("ORDER-001", "Nilpeter/Lesko") with { Status = DieCutStatus.OrderNew },
            fixture.EmployeeId);

        Assert.NotNull(updated);
        Assert.Equal(DieCutStatus.OrderNew, updated.Status);
    }

    [Fact]
    public void CalculateRunMetrics_UsesQuantityStreamsHeightGapAndShaft()
    {
        var result = DieCutCalculations.CalculateRunMetrics(
            quantity: 3_500,
            streams: 4,
            labelLengthMm: 74,
            interLabelSpacingMeters: 0.0022m,
            shaft: 96);

        Assert.Equal(66.675m, result.RunLengthMeters);
        Assert.Equal(219, result.Revolutions);
    }

    [Fact]
    public async Task AddCirculation_SumsQuantityMetersAndRevolutionsAndWritesEvents()
    {
        await using var fixture = CreateFixture();
        var created = await fixture.Service.CreateAsync(NewDieCut("001", "Nilpeter/Lesko"), fixture.EmployeeId);

        await fixture.Service.AddCirculationAsync(created.Id, 1_000, fixture.EmployeeId);
        var updated = await fixture.Service.AddCirculationAsync(created.Id, 2_500, fixture.EmployeeId);
        var events = await fixture.Service.GetEventsAsync(created.Id);

        Assert.NotNull(updated);
        Assert.Equal(3_500, updated.Mileage);
        Assert.Equal(66.675m, updated.RunLengthMeters);
        Assert.Equal(220, updated.Revolutions);
        Assert.NotNull(events);
        var circulationEvents = events.Where(item => item.Type == DieCutEventType.CirculationAdded).ToList();
        Assert.Equal(2, circulationEvents.Count);
        Assert.All(circulationEvents, item => Assert.Equal(DieCutEventType.CirculationAdded, item.Type));
        Assert.Equal(2_500, circulationEvents[0].Quantity);
        Assert.Equal(3_500, events[0].MileageAfter);
        Assert.Equal(66.675m, events[0].RunLengthMetersAfter);
        Assert.Equal(220, events[0].RevolutionsAfter);
        Assert.Equal("Adrian Test", events[0].EmployeeName);
    }

    [Fact]
    public async Task InstallReplacement_RequiresOrderAndPreservesLifetimeResource()
    {
        await using var fixture = CreateFixture();
        var command = NewDieCut("001", "Nilpeter/Lesko");
        var created = await fixture.Service.CreateAsync(command, fixture.EmployeeId);
        var used = await fixture.Service.AddCirculationAsync(created.Id, 7_500, fixture.EmployeeId);

        await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.Service.InstallReplacementAsync(created.Id, fixture.EmployeeId));

        await fixture.Service.UpdateAsync(
            created.Id, command with { Status = DieCutStatus.OrderNew }, fixture.EmployeeId);
        var replaced = await fixture.Service.InstallReplacementAsync(created.Id, fixture.EmployeeId);
        var events = await fixture.Service.GetEventsAsync(created.Id);
        var replacementEvent = Assert.Single(events!, item => item.Type == DieCutEventType.ReplacementInstalled);

        Assert.NotNull(used);
        Assert.NotNull(replaced);
        Assert.Equal(0, replaced.Mileage);
        Assert.Equal(0, replaced.RunLengthMeters);
        Assert.Equal(0, replaced.Revolutions);
        Assert.Equal(7_500, replaced.LifetimeMileage);
        Assert.Equal(142.875m, replaced.LifetimeRunLengthMeters);
        Assert.Equal(469, replaced.LifetimeRevolutions);
        Assert.Equal(2, replaced.Generation);
        Assert.Equal(DieCutStatus.Active, replaced.Status);
        Assert.Equal(7_500, replacementEvent.MileageBefore);
        Assert.Equal(0, replacementEvent.MileageAfter);
    }

    [Fact]
    public async Task AddCirculation_ReachingInspectionThresholdChangesStatus()
    {
        await using var fixture = CreateFixture();
        var created = await fixture.Service.CreateAsync(NewDieCut("001", "Nilpeter/Lesko"), fixture.EmployeeId);
        var entity = await fixture.DbContext.DieCuts.SingleAsync(x => x.Id == created.Id);
        entity.Revolutions = 999_999;
        entity.LifetimeRevolutions = 999_999;
        await fixture.DbContext.SaveChangesAsync();

        var updated = await fixture.Service.AddCirculationAsync(created.Id, 100, fixture.EmployeeId);

        Assert.NotNull(updated);
        Assert.Equal(DieCutStatus.NeedsInspection, updated.Status);
        Assert.True(updated.Revolutions >= 1_000_000);
        Assert.Equal(updated.Revolutions, updated.LifetimeRevolutions);
    }
    [Fact]
    public async Task Retire_WritesEventAndBlocksFurtherOperations()
    {
        await using var fixture = CreateFixture();
        var created = await fixture.Service.CreateAsync(NewDieCut("001", "Nilpeter/Lesko"), fixture.EmployeeId);
        await fixture.Service.AddCirculationAsync(created.Id, 500, fixture.EmployeeId);

        var retired = await fixture.Service.RetireAsync(created.Id, fixture.EmployeeId);
        var events = await fixture.Service.GetEventsAsync(created.Id);

        Assert.NotNull(retired);
        Assert.Equal(DieCutStatus.Retired, retired.Status);
        Assert.Contains(events!, item => item.Type == DieCutEventType.Retired && item.MileageAfter == 500);
        await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.Service.AddCirculationAsync(created.Id, 100, fixture.EmployeeId));
        await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.Service.UpdateAsync(created.Id, NewDieCut("001", "Nilpeter/Lesko"), fixture.EmployeeId));
    }

    [Fact]
    public async Task Delete_HidesKnifeFromCatalogAndPreservesAuditEvent()
    {
        await using var fixture = CreateFixture();
        var created = await fixture.Service.CreateAsync(
            NewDieCut("DEL-001", "Nilpeter/Lesko"), fixture.EmployeeId);
        await fixture.Service.AddCirculationAsync(created.Id, 1_200, fixture.EmployeeId);

        var deleted = await fixture.Service.DeleteAsync(created.Id, fixture.EmployeeId);
        var search = await fixture.Service.SearchAsync(new DieCutQuery(
            null, null, null, null, null, null, null, null, null, null, 1, 50));
        var facets = await fixture.Service.GetFacetsAsync();
        var events = await fixture.Service.GetEventsAsync(created.Id);

        Assert.NotNull(deleted);
        Assert.Equal(DieCutStatus.Deleted, deleted.Status);
        Assert.Equal(0, search.Total);
        Assert.DoesNotContain("Nilpeter/Lesko", facets.Equipment);
        Assert.Contains(events!, item =>
            item.Type == DieCutEventType.Deleted
            && item.EmployeeId == fixture.EmployeeId
            && item.MileageAfter == 1_200);
    }

    [Fact]
    public async Task Create_RejectsEquipmentAndFigureOutsideFixedLists()
    {
        await using var fixture = CreateFixture();

        await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.Service.CreateAsync(NewDieCut("BAD-E", "Unknown"), fixture.EmployeeId));
        await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.Service.CreateAsync(
                NewDieCut("BAD-F", "MarkAndy") with { Figure = "овал" },
                fixture.EmployeeId));
    }

    [Fact]
    public async Task Update_CannotRetireWithoutDedicatedOperation()
    {
        await using var fixture = CreateFixture();
        var created = await fixture.Service.CreateAsync(NewDieCut("001", "Nilpeter/Lesko"), fixture.EmployeeId);
        var command = NewDieCut("001", "Nilpeter/Lesko") with { Status = DieCutStatus.Retired };

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.Service.UpdateAsync(created.Id, command, fixture.EmployeeId));

        Assert.Contains("защищённую операцию", exception.Message);
    }

    private static SaveDieCutCommand NewDieCut(
        string number,
        string equipment,
        string material = "Paper",
        decimal x = 58,
        string? jcOrderNumber = null) => new(
        number,
        JcOrderNumber: jcOrderNumber,
        equipment,
        Shaft: 96,
        X: x,
        Y: 74,
        Streams: 4,
        Repeats: 4,
        GrooveSpacing: 2,
        LabelCornerRadius: 1.5m,
        Material: material,
        H: 430,
        Figure: "прямоугольник",
        Comments: null,
        Date: new DateOnly(2026, 7, 20),
        Status: DieCutStatus.Active);

    private static TestFixture CreateFixture()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var dbContext = new CatalogDbContext(options);
        var employeeId = Guid.NewGuid();
        dbContext.Employees.Add(new Employee
        {
            Id = employeeId,
            Email = "adrian@example.test",
            NormalizedEmail = "ADRIAN@EXAMPLE.TEST",
            PasswordHash = "test",
            FirstName = "Adrian",
            LastName = "Test"
        });
        dbContext.Equipment.AddRange(new[] { "Nilpeter/Lesko", "NilPeter", "MarkAndy", "Big Lesko", "Label Source" }
            .Select(name => new Equipment { Name = name, NormalizedName = name.ToUpperInvariant() }));
        dbContext.CatalogReferenceEntries.AddRange(
            new CatalogReferenceEntry { Kind = CatalogReferenceKind.Material, Name = "Paper", NormalizedName = "PAPER" },
            new CatalogReferenceEntry { Kind = CatalogReferenceKind.Material, Name = "TTOP", NormalizedName = "TTOP" },
            new CatalogReferenceEntry { Kind = CatalogReferenceKind.Material, Name = "PP60 TOP WHITE", NormalizedName = "PP60 TOP WHITE" },
            new CatalogReferenceEntry { Kind = CatalogReferenceKind.Figure, Name = "прямоугольник", NormalizedName = "ПРЯМОУГОЛЬНИК" });
        dbContext.SaveChanges();
        return new TestFixture(dbContext, new DieCutCatalogService(dbContext), employeeId);
    }

    private sealed class TestFixture(CatalogDbContext dbContext, DieCutCatalogService service, Guid employeeId)
        : IAsyncDisposable
    {
        public CatalogDbContext DbContext { get; } = dbContext;
        public DieCutCatalogService Service { get; } = service;
        public Guid EmployeeId { get; } = employeeId;
        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }
}