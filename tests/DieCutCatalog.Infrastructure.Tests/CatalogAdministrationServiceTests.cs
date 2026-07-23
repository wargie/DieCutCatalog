using System.ComponentModel.DataAnnotations;
using System.Text;
using DieCutCatalog.Application.Catalog;
using DieCutCatalog.Domain.Catalog;
using DieCutCatalog.Domain.Employees;
using DieCutCatalog.Infrastructure.Catalog;
using DieCutCatalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DieCutCatalog.Infrastructure.Tests;

public sealed class CatalogAdministrationServiceTests
{
    [Fact]
    public async Task RenameMaterial_UpdatesExistingKnives()
    {
        await using var fixture = CreateFixture();
        var knife = await fixture.Catalog.CreateAsync(NewKnife(), fixture.EmployeeId);
        var references = await fixture.Administration.GetReferencesAsync();
        var material = Assert.Single(references.Materials);

        await fixture.Administration.RenameReferenceAsync(
            CatalogReferenceType.Material, material.Id, "Paper Premium");
        var updated = await fixture.Catalog.GetAsync(knife.Id);

        Assert.Equal("Paper Premium", updated!.Material);
    }

    [Fact]
    public async Task DeleteReference_RemovesUnusedValue()
    {
        await using var fixture = CreateFixture();
        var added = await fixture.Administration.AddReferenceAsync(
            CatalogReferenceType.Material, "Unused material");

        var deleted = await fixture.Administration.DeleteReferenceAsync(
            CatalogReferenceType.Material, added.Id);
        var references = await fixture.Administration.GetReferencesAsync();

        Assert.True(deleted);
        Assert.DoesNotContain(references.Materials, x => x.Id == added.Id);
    }

    [Fact]
    public async Task DeleteReference_RejectsValueUsedByKnife()
    {
        await using var fixture = CreateFixture();
        await fixture.Catalog.CreateAsync(NewKnife(), fixture.EmployeeId);
        var references = await fixture.Administration.GetReferencesAsync();
        var material = Assert.Single(references.Materials);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.Administration.DeleteReferenceAsync(CatalogReferenceType.Material, material.Id));

        Assert.Contains("используется", exception.Message);
    }
    [Fact]
    public async Task AuditLog_ContainsEventsAndExportsCsvAndPdf()
    {
        await using var fixture = CreateFixture();
        await fixture.Catalog.CreateAsync(NewKnife(), fixture.EmployeeId);

        var log = await fixture.Administration.SearchAuditLogAsync("001", 1, 50);
        var csv = await fixture.Administration.ExportAuditLogAsync("001", false);
        var pdf = await fixture.Administration.ExportAuditLogAsync("001", true);

        Assert.Single(log.Items);
        Assert.Equal(DieCutEventType.Created, log.Items[0].Type);
        Assert.Contains("Нож создан", Encoding.UTF8.GetString(csv.Content));
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf.Content, 0, 4));
    }

    private static Fixture CreateFixture()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var dbContext = new CatalogDbContext(options);
        var employeeId = Guid.NewGuid();
        dbContext.Employees.Add(new Employee
        {
            Id = employeeId,
            Email = "admin@example.test",
            NormalizedEmail = "ADMIN@EXAMPLE.TEST",
            PasswordHash = "test",
            FirstName = "Adrian",
            LastName = "Test",
            Role = EmployeeRole.Administrator
        });
        dbContext.Equipment.Add(new Equipment
        {
            Name = "Nilpeter/Lesko",
            NormalizedName = "NILPETER/LESKO"
        });
        dbContext.CatalogReferenceEntries.AddRange(
            new CatalogReferenceEntry
            {
                Kind = CatalogReferenceKind.Material,
                Name = "Paper",
                NormalizedName = "PAPER"
            },
            new CatalogReferenceEntry
            {
                Kind = CatalogReferenceKind.Figure,
                Name = "прямоугольник",
                NormalizedName = "ПРЯМОУГОЛЬНИК"
            });
        dbContext.SaveChanges();
        return new Fixture(
            dbContext,
            new DieCutCatalogService(dbContext),
            new CatalogAdministrationService(dbContext),
            employeeId);
    }

    private static SaveDieCutCommand NewKnife() => new(
        "001", null, "Nilpeter/Lesko", 96, 58, 74, 4, 4, 2, 1.5m,
        "Paper", 430, "прямоугольник", null, new DateOnly(2026, 7, 23),
        DieCutStatus.Active);

    private sealed class Fixture(
        CatalogDbContext dbContext,
        DieCutCatalogService catalog,
        CatalogAdministrationService administration,
        Guid employeeId) : IAsyncDisposable
    {
        public CatalogDbContext DbContext { get; } = dbContext;
        public DieCutCatalogService Catalog { get; } = catalog;
        public CatalogAdministrationService Administration { get; } = administration;
        public Guid EmployeeId { get; } = employeeId;
        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }
}
