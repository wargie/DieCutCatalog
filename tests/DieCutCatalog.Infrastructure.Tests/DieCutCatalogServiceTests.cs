using System.ComponentModel.DataAnnotations;
using DieCutCatalog.Application.Catalog;
using DieCutCatalog.Domain.Catalog;
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
        await fixture.Service.CreateAsync(NewDieCut("001", "NilPeter"), fixture.EmployeeId);

        await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.Service.CreateAsync(NewDieCut(" 001 ", "nilpeter"), fixture.EmployeeId));

        var otherEquipment = await fixture.Service.CreateAsync(NewDieCut("001", "Lesko"), fixture.EmployeeId);
        Assert.Equal("Lesko", otherEquipment.Equipment);
    }

    [Fact]
    public async Task Create_CalculatesXAndYLikeExcel()
    {
        await using var fixture = CreateFixture();

        var created = await fixture.Service.CreateAsync(
            NewDieCut("010", "Lesko", x: 101), fixture.EmployeeId);

        Assert.Equal(0.026m, created.GapX);
        Assert.Equal(0.0022m, created.GapY);
    }

    [Fact]
    public async Task Search_AppliesFiltersAndPagination()
    {
        await using var fixture = CreateFixture();
        await fixture.Service.CreateAsync(NewDieCut("001", "NilPeter", material: "Paper", x: 58), fixture.EmployeeId);
        await fixture.Service.CreateAsync(NewDieCut("002", "NilPeter", material: "TTOP", x: 80), fixture.EmployeeId);
        await fixture.Service.CreateAsync(NewDieCut("003", "Lesko", material: "Paper", x: 100), fixture.EmployeeId);

        var result = await fixture.Service.SearchAsync(new DieCutQuery(
            Search: null,
            Equipment: "NilPeter",
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
    }

    [Fact]
    public async Task Update_ChangesCardAndCreatesEquipmentFacet()
    {
        await using var fixture = CreateFixture();
        var created = await fixture.Service.CreateAsync(NewDieCut("001", "NilPeter"), fixture.EmployeeId);
        var command = NewDieCut("001A", "MarkAndy", material: "PP60 TOP WHITE", x: 85) with
        {
            Comments = "RLL",
            Status = DieCutStatus.NeedsInspection
        };

        var updated = await fixture.Service.UpdateAsync(created.Id, command, fixture.EmployeeId);
        var facets = await fixture.Service.GetFacetsAsync();

        Assert.NotNull(updated);
        Assert.Equal("001A", updated.Number);
        Assert.Equal("MarkAndy", updated.Equipment);
        Assert.Equal(DieCutStatus.NeedsInspection, updated.Status);
        Assert.Contains("MarkAndy", facets.Equipment);
        Assert.Contains("PP60 TOP WHITE", facets.Materials);
    }

    private static SaveDieCutCommand NewDieCut(
        string number,
        string equipment,
        string material = "Paper",
        decimal x = 58) => new(
        number,
        equipment,
        Shaft: 96,
        X: x,
        Y: 74,
        Streams: 4,
        Repeats: 4,
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
        return new TestFixture(dbContext, new DieCutCatalogService(dbContext), Guid.NewGuid());
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
