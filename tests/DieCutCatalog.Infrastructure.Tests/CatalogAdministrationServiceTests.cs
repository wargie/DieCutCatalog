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
    public async Task ImportReferences_AddsUniqueValuesAndSkipsDuplicates()
    {
        await using var fixture = CreateFixture();

        var result = await fixture.Administration.ImportReferencesAsync(
            CatalogReferenceType.Material, ["Paper", "Clear PET30", " clear pet30 ", "White PET", ""]);
        var references = await fixture.Administration.GetReferencesAsync();

        Assert.Equal(2, result.Added);
        Assert.Equal(3, result.Skipped);
        Assert.Contains(references.Materials, x => x.Name == "Clear PET30");
        Assert.Contains(references.Materials, x => x.Name == "White PET");
    }

    [Fact]
    public async Task ReferenceArticle_CanBeSavedAndRead()
    {
        await using var fixture = CreateFixture();
        var material = Assert.Single((await fixture.Administration.GetReferencesAsync()).Materials);

        var updated = await fixture.Administration.UpdateReferenceArticleAsync(
            CatalogReferenceType.Material, material.Id, @"{\rtf1 Описание материала}");
        var refreshed = Assert.Single((await fixture.Administration.GetReferencesAsync()).Materials);

        Assert.True(updated);
        Assert.Equal(@"{\rtf1 Описание материала}", refreshed.ArticleRtf);
    }

    [Fact]
    public async Task CustomDirectory_CreatesGroupDirectoryAndValues()
    {
        await using var fixture = CreateFixture();
        var group = await fixture.Administration.AddDirectoryGroupAsync("Производство");
        var directory = await fixture.Administration.AddDirectoryAsync(
            new CreateReferenceDirectoryCommand(group.Id, "Причины списания", "Причины вывода ножа из эксплуатации"));
        var value = await fixture.Administration.AddDirectoryValueAsync(directory.Id, "Естественный износ");

        var overview = await fixture.Administration.GetDirectoryOverviewAsync();
        var values = await fixture.Administration.GetDirectoryValuesAsync(directory.Id, false);

        Assert.Equal(group.Id, Assert.Single(overview.Groups).Id);
        Assert.Equal(1, Assert.Single(overview.Directories).ValueCount);
        Assert.Equal(value.Id, Assert.Single(values).Id);
    }

    [Fact]
    public async Task DeleteDirectoryGroup_PreservesDirectoriesWithoutGroup()
    {
        await using var fixture = CreateFixture();
        var group = await fixture.Administration.AddDirectoryGroupAsync("Временная группа");
        var directory = await fixture.Administration.AddDirectoryAsync(
            new CreateReferenceDirectoryCommand(group.Id, "Сохраняемый справочник", null));

        var deleted = await fixture.Administration.DeleteDirectoryGroupAsync(group.Id);
        var overview = await fixture.Administration.GetDirectoryOverviewAsync();

        Assert.True(deleted);
        Assert.Empty(overview.Groups);
        var preserved = Assert.Single(overview.Directories);
        Assert.Equal(directory.Id, preserved.Id);
        Assert.Null(preserved.GroupId);
    }

    [Fact]
    public async Task CustomDirectoryValue_CanBeArchivedAndRestored()
    {
        await using var fixture = CreateFixture();
        var directory = await fixture.Administration.AddDirectoryAsync(
            new CreateReferenceDirectoryCommand(null, "Поставщики", null));
        var value = await fixture.Administration.AddDirectoryValueAsync(directory.Id, "JustCut");

        await fixture.Administration.UpdateDirectoryValueAsync(directory.Id, value.Id, value.Name, true);
        Assert.Empty(await fixture.Administration.GetDirectoryValuesAsync(directory.Id, false));
        Assert.True(Assert.Single(await fixture.Administration.GetDirectoryValuesAsync(directory.Id, true)).IsArchived);

        await fixture.Administration.UpdateDirectoryValueAsync(directory.Id, value.Id, value.Name, false);
        Assert.False(Assert.Single(await fixture.Administration.GetDirectoryValuesAsync(directory.Id, false)).IsArchived);
    }

    [Fact]
    public async Task ImportDirectoryValues_AddsUniqueValuesAndPreservesOrder()
    {
        await using var fixture = CreateFixture();
        var directory = await fixture.Administration.AddDirectoryAsync(
            new CreateReferenceDirectoryCommand(null, "Материалы поставщика", null));
        await fixture.Administration.AddDirectoryValueAsync(directory.Id, "Existing");

        var result = await fixture.Administration.ImportDirectoryValuesAsync(
            directory.Id, ["Existing", "Clear PET30", "clear pet30", "White PET", ""]);
        var values = await fixture.Administration.GetDirectoryValuesAsync(directory.Id, false);

        Assert.Equal(2, result.Added);
        Assert.Equal(3, result.Skipped);
        Assert.Equal(["Existing", "Clear PET30", "White PET"], values.Select(x => x.Name));
    }

    [Fact]
    public async Task DeleteDirectoryValue_RemovesSelectedValue()
    {
        await using var fixture = CreateFixture();
        var directory = await fixture.Administration.AddDirectoryAsync(
            new CreateReferenceDirectoryCommand(null, "Удаляемые значения", null));
        var value = await fixture.Administration.AddDirectoryValueAsync(directory.Id, "Временная позиция");

        var deleted = await fixture.Administration.DeleteDirectoryValueAsync(directory.Id, value.Id);
        var values = await fixture.Administration.GetDirectoryValuesAsync(directory.Id, true);

        Assert.True(deleted);
        Assert.Empty(values);
    }

    [Fact]
    public async Task DirectoryValueArticle_CanBeSavedAndRead()
    {
        await using var fixture = CreateFixture();
        var directory = await fixture.Administration.AddDirectoryAsync(
            new CreateReferenceDirectoryCommand(null, "Карточки", null));
        var value = await fixture.Administration.AddDirectoryValueAsync(directory.Id, "Clear PET30");

        var updated = await fixture.Administration.UpdateDirectoryValueArticleAsync(
            directory.Id, value.Id, @"{\rtf1 Техническое описание}");
        var refreshed = Assert.Single(await fixture.Administration.GetDirectoryValuesAsync(directory.Id, true));

        Assert.True(updated);
        Assert.Equal(@"{\rtf1 Техническое описание}", refreshed.ArticleRtf);
    }

    [Fact]
    public async Task DeleteCustomDirectory_RejectsNonEmptyDirectory()
    {
        await using var fixture = CreateFixture();
        var directory = await fixture.Administration.AddDirectoryAsync(
            new CreateReferenceDirectoryCommand(null, "Тип дефекта", null));
        await fixture.Administration.AddDirectoryValueAsync(directory.Id, "Скол");

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => fixture.Administration.DeleteDirectoryAsync(directory.Id));

        Assert.Contains("непустой", exception.Message);
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

    [Fact]
    public async Task AuditLog_IncludesEmployeeLoginAndExportsIt()
    {
        await using var fixture = CreateFixture();
        fixture.DbContext.EmployeeAccessEvents.Add(new EmployeeAccessEvent
        {
            EmployeeId = fixture.EmployeeId,
            Type = EmployeeAccessEventType.LoggedIn
        });
        await fixture.DbContext.SaveChangesAsync();

        var log = await fixture.Administration.SearchAuditLogAsync("Adrian", 1, 50);
        var csv = await fixture.Administration.ExportAuditLogAsync("Adrian", false);
        var pdf = await fixture.Administration.ExportAuditLogAsync("Adrian", true);

        var entry = Assert.Single(log.Items);
        Assert.Equal(EmployeeAccessEventType.LoggedIn, entry.AccessType);
        Assert.Contains("Вход в систему", Encoding.UTF8.GetString(csv.Content));
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
