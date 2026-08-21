using DieCutCatalog.Domain.Catalog;
using DieCutCatalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DieCutCatalog.PostgreSql.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class InspectionResourceUpgradePostgreSqlTests(PostgreSqlFixture fixture)
{
    private const string LastVersion193Migration = "20260818101310_AddUniversalAuditEvents";

    [Fact]
    public async Task Upgrade_to_194_normalizes_threshold_and_resets_only_current_counters()
    {
        var connectionString = await fixture.CreateIsolatedDatabaseConnectionStringAsync();
        var equipment = new Equipment { Name = "Upgrade equipment", NormalizedName = "UPGRADE EQUIPMENT" };
        var millionThreshold = NewDieCut(equipment, "RESOURCE-1000000", 1_000_000, DieCutStatus.Active);
        var halfMillionThreshold = NewDieCut(equipment, "RESOURCE-500000", 500_000, DieCutStatus.NeedsInspection);

        await using (var legacyContext = fixture.CreateDbContext(connectionString))
        {
            await legacyContext.GetService<IMigrator>().MigrateAsync(LastVersion193Migration);
            legacyContext.DieCuts.AddRange(millionThreshold, halfMillionThreshold);
            await legacyContext.SaveChangesAsync();

            await legacyContext.Database.MigrateAsync();
        }

        await using var currentContext = fixture.CreateDbContext(connectionString);
        var stored = await currentContext.DieCuts.AsNoTracking()
            .Where(x => x.Id == millionThreshold.Id || x.Id == halfMillionThreshold.Id)
            .OrderBy(x => x.Number)
            .ToListAsync();

        Assert.Equal(2, stored.Count);
        Assert.All(stored, dieCut =>
        {
            Assert.Equal(500_000, dieCut.NextInspectionRevolutions);
            Assert.Equal(0, dieCut.Mileage);
            Assert.Equal(0, dieCut.RunLengthMeters);
            Assert.Equal(0, dieCut.Revolutions);
        });
        Assert.Equal(1_200_000, stored[0].LifetimeMileage);
        Assert.Equal(12_345.678m, stored[0].LifetimeRunLengthMeters);
        Assert.Equal(750_000, stored[0].LifetimeRevolutions);
        Assert.Equal(DieCutStatus.Active, stored[0].Status);
        Assert.Equal(1_200_000, stored[1].LifetimeMileage);
        Assert.Equal(12_345.678m, stored[1].LifetimeRunLengthMeters);
        Assert.Equal(750_000, stored[1].LifetimeRevolutions);
        Assert.Equal(DieCutStatus.NeedsInspection, stored[1].Status);
    }

    private static DieCut NewDieCut(
        Equipment equipment,
        string number,
        long nextInspectionRevolutions,
        DieCutStatus status) => new()
        {
            Number = number,
            NormalizedNumber = number,
            Equipment = equipment,
            EquipmentId = equipment.Id,
            Shaft = 96,
            X = 100,
            Y = 150,
            Streams = 2,
            Repeats = 2,
            Material = "paper semigloss",
            H = 220,
            Figure = "прямоугольник",
            Mileage = 200_000,
            RunLengthMeters = 1_234.567m,
            Revolutions = 125_000,
            LifetimeMileage = 1_200_000,
            LifetimeRunLengthMeters = 12_345.678m,
            LifetimeRevolutions = 750_000,
            NextInspectionRevolutions = nextInspectionRevolutions,
            Status = status,
            CreatedByEmployeeId = Guid.NewGuid(),
            UpdatedByEmployeeId = Guid.NewGuid()
        };
}
