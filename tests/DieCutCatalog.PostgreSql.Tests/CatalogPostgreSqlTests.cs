using System.ComponentModel.DataAnnotations;
using DieCutCatalog.Application.Catalog;
using DieCutCatalog.Domain.Catalog;
using DieCutCatalog.Domain.Employees;
using DieCutCatalog.Infrastructure.Catalog;
using DieCutCatalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DieCutCatalog.PostgreSql.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class CatalogPostgreSqlTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Migrations_apply_cleanly_and_leave_database_current()
    {
        await using var dbContext = fixture.CreateDbContext();

        var applied = await dbContext.Database.GetAppliedMigrationsAsync();
        var pending = await dbContext.Database.GetPendingMigrationsAsync();

        Assert.NotEmpty(applied);
        Assert.Empty(pending);
        Assert.Equal(dbContext.Database.GetMigrations().Count(), applied.Count());
    }

    [Fact]
    public async Task Filtered_unique_index_allows_number_reuse_only_after_deletion()
    {
        var equipment = await AddEquipmentAsync();
        var first = NewDieCut(equipment, "FILTERED-INDEX", DieCutStatus.Active);
        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.DieCuts.Add(first);
            await dbContext.SaveChangesAsync();
        }

        await using (var duplicateContext = fixture.CreateDbContext())
        {
            duplicateContext.DieCuts.Add(NewDieCut(equipment, first.Number, DieCutStatus.Active));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
            var postgres = Assert.IsType<PostgresException>(exception.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        }

        await using (var deleteContext = fixture.CreateDbContext())
        {
            var stored = await deleteContext.DieCuts.SingleAsync(x => x.Id == first.Id);
            stored.Status = DieCutStatus.Deleted;
            await deleteContext.SaveChangesAsync();
        }

        await using (var replacementContext = fixture.CreateDbContext())
        {
            replacementContext.DieCuts.Add(NewDieCut(equipment, first.Number, DieCutStatus.Active));
            await replacementContext.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Service_duplicate_check_is_case_insensitive_on_PostgreSQL()
    {
        var seed = await AddServiceReferencesAsync();
        await using var dbContext = fixture.CreateDbContext();
        var service = new DieCutCatalogService(dbContext);

        await service.CreateAsync(NewCommand("Case-Number", seed.EquipmentName, seed.Material, seed.Figure), seed.EmployeeId);
        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(NewCommand("case-number", seed.EquipmentName, seed.Material, seed.Figure), seed.EmployeeId));

        Assert.Contains("уже существует", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Numeric_precision_is_enforced_by_PostgreSQL()
    {
        var equipment = await AddEquipmentAsync();
        var dieCut = NewDieCut(equipment, "PRECISION", DieCutStatus.Active);
        dieCut.X = 12.3456m;

        await using (var writeContext = fixture.CreateDbContext())
        {
            writeContext.DieCuts.Add(dieCut);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateDbContext();
        var stored = await readContext.DieCuts.AsNoTracking().SingleAsync(x => x.Id == dieCut.Id);
        Assert.Equal(12.346m, stored.X);
    }

    [Fact]
    public async Task Concurrent_circulation_updates_are_serialized_without_lost_updates_or_deadlocks()
    {
        var seed = await AddServiceReferencesAsync();
        Guid dieCutId;
        await using (var createContext = fixture.CreateDbContext())
        {
            var service = new DieCutCatalogService(createContext);
            var created = await service.CreateAsync(
                NewCommand("CONCURRENT", seed.EquipmentName, seed.Material, seed.Figure),
                seed.EmployeeId);
            dieCutId = created.Id;
        }

        const int writers = 20;
        const long quantityPerWriter = 1_000;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var updates = Enumerable.Range(0, writers).Select(async _ =>
        {
            await start.Task.WaitAsync(timeout.Token);
            await using var context = fixture.CreateDbContext();
            var service = new DieCutCatalogService(context);
            return await service.AddCirculationAsync(
                dieCutId,
                quantityPerWriter,
                seed.EmployeeId,
                timeout.Token);
        }).ToArray();

        start.SetResult();
        await Task.WhenAll(updates);

        await using var verificationContext = fixture.CreateDbContext();
        var stored = await verificationContext.DieCuts.AsNoTracking().SingleAsync(x => x.Id == dieCutId);
        var events = await verificationContext.DieCutEvents.AsNoTracking()
            .Where(x => x.DieCutId == dieCutId && x.Type == DieCutEventType.CirculationAdded)
            .ToListAsync();

        Assert.Equal(writers * quantityPerWriter, stored.Mileage);
        Assert.Equal(writers, events.Count);
        Assert.Equal(
            Enumerable.Range(0, writers).Select(index => index * quantityPerWriter),
            events.Select(x => x.MileageBefore).Order());
        Assert.Equal(
            Enumerable.Range(1, writers).Select(index => index * quantityPerWriter),
            events.Select(x => x.MileageAfter).Order());
    }

    [Fact]
    public async Task Circulation_and_event_are_rolled_back_together_when_event_insert_fails()
    {
        var seed = await AddServiceReferencesAsync();
        Guid dieCutId;
        await using (var createContext = fixture.CreateDbContext())
        {
            var service = new DieCutCatalogService(createContext);
            var created = await service.CreateAsync(
                NewCommand("TRANSACTION", seed.EquipmentName, seed.Material, seed.Figure),
                seed.EmployeeId);
            dieCutId = created.Id;
        }

        await using (var updateContext = fixture.CreateDbContext())
        {
            var service = new DieCutCatalogService(updateContext);
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                service.AddCirculationAsync(dieCutId, 1_000, Guid.NewGuid()));
        }

        await using var verificationContext = fixture.CreateDbContext();
        var stored = await verificationContext.DieCuts.AsNoTracking().SingleAsync(x => x.Id == dieCutId);
        var circulationEvents = await verificationContext.DieCutEvents.AsNoTracking()
            .CountAsync(x => x.DieCutId == dieCutId && x.Type == DieCutEventType.CirculationAdded);

        Assert.Equal(0, stored.Mileage);
        Assert.Equal(0, circulationEvents);
    }
    private async Task<Equipment> AddEquipmentAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var equipment = new Equipment
        {
            Name = $"Equipment-{suffix}",
            NormalizedName = $"EQUIPMENT-{suffix.ToUpperInvariant()}"
        };
        await using var dbContext = fixture.CreateDbContext();
        dbContext.Equipment.Add(equipment);
        await dbContext.SaveChangesAsync();
        return equipment;
    }

    private async Task<ServiceSeed> AddServiceReferencesAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var employee = new Employee
        {
            Email = $"employee-{suffix}@example.test",
            NormalizedEmail = $"EMPLOYEE-{suffix.ToUpperInvariant()}@EXAMPLE.TEST",
            PasswordHash = "integration-test",
            FirstName = "Integration",
            LastName = "Test"
        };
        var equipment = new Equipment
        {
            Name = $"Equipment-{suffix}",
            NormalizedName = $"EQUIPMENT-{suffix.ToUpperInvariant()}"
        };
        var material = $"Material-{suffix}";
        var figure = $"Figure-{suffix}";

        await using var dbContext = fixture.CreateDbContext();
        dbContext.AddRange(
            employee,
            equipment,
            new CatalogReferenceEntry
            {
                Kind = CatalogReferenceKind.Material,
                Name = material,
                NormalizedName = material.ToUpperInvariant()
            },
            new CatalogReferenceEntry
            {
                Kind = CatalogReferenceKind.Figure,
                Name = figure,
                NormalizedName = figure.ToUpperInvariant()
            });
        await dbContext.SaveChangesAsync();
        return new ServiceSeed(employee.Id, equipment.Name, material, figure);
    }

    private static SaveDieCutCommand NewCommand(string number, string equipment, string material, string figure) => new(
        number,
        JcOrderNumber: null,
        equipment,
        Shaft: 96,
        X: 50,
        Y: 70,
        Streams: 2,
        Repeats: 4,
        GrooveSpacing: 2,
        LabelCornerRadius: 2,
        material,
        H: 220,
        figure,
        Comments: null,
        Date: new DateOnly(2026, 7, 31),
        Status: DieCutStatus.Active);

    private static DieCut NewDieCut(Equipment equipment, string number, DieCutStatus status) => new()
    {
        Number = number,
        NormalizedNumber = number.ToUpperInvariant(),
        EquipmentId = equipment.Id,
        Shaft = 96,
        X = 50,
        Y = 70,
        Streams = 2,
        Repeats = 4,
        GrooveSpacing = 2,
        LabelCornerRadius = 2,
        GapX = 0.116m,
        GapY = 0.0062m,
        Material = "Integration material",
        H = 220,
        Figure = "прямоугольник",
        Status = status,
        CreatedByEmployeeId = Guid.NewGuid(),
        UpdatedByEmployeeId = Guid.NewGuid()
    };

    private sealed record ServiceSeed(Guid EmployeeId, string EquipmentName, string Material, string Figure);
}