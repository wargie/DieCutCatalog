using DieCutCatalog.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DieCutCatalog.PostgreSql.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class AuditEventUpgradePostgreSqlTests(PostgreSqlFixture fixture)
{
    private const string ReferencePositionAuditMigration = "20260818092515_AddReferencePositionAudit";

    [Fact]
    public async Task Upgrade_preserves_reference_position_events_in_universal_audit()
    {
        var connectionString = await fixture.CreateIsolatedDatabaseConnectionStringAsync();
        var employeeId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        await using (var legacyContext = fixture.CreateDbContext(connectionString))
        {
            await legacyContext.GetService<IMigrator>().MigrateAsync(ReferencePositionAuditMigration);
            await legacyContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO employees
                    ("Id", "Email", "NormalizedEmail", "PasswordHash", "MustChangePassword",
                     "Role", "IsActive", "FirstName", "LastName", "CreatedAt", "UpdatedAt")
                VALUES
                    ({employeeId}, {"audit-upgrade@example.test"}, {"AUDIT-UPGRADE@EXAMPLE.TEST"},
                     {"unused-password-hash"}, {false}, {"Operator"}, {true},
                     {"Audit"}, {"Upgrade"}, {occurredAt}, {occurredAt});
                """);
            await legacyContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO reference_position_events
                    ("Id", "EmployeeId", "Type", "SourcePositionId", "DestinationPositionId",
                     "SourceName", "DestinationName", "SourceSection", "DestinationSection", "OccurredAt")
                VALUES
                    ({eventId}, {employeeId}, {"Moved"}, {sourceId}, {destinationId},
                     {"Old material"}, {"New material"}, {"Материалы"}, {"Материалы"}, {occurredAt});
                """);

            await legacyContext.Database.MigrateAsync();
        }

        await using var currentContext = fixture.CreateDbContext(connectionString);
        var auditEvent = await currentContext.AuditEvents.AsNoTracking()
            .SingleAsync(x => x.Id == eventId);

        Assert.Equal(employeeId, auditEvent.ActorEmployeeId);
        Assert.Null(auditEvent.ApproverEmployeeId);
        Assert.Equal(AuditEntityType.Material, auditEvent.EntityType);
        Assert.Equal(destinationId, auditEvent.EntityId);
        Assert.Equal(AuditAction.Moved, auditEvent.Action);
        Assert.Equal(eventId, auditEvent.CorrelationId);
        Assert.Contains(sourceId.ToString(), auditEvent.BeforeJson);
        Assert.Contains("Old material", auditEvent.BeforeJson);
        Assert.Contains(destinationId.ToString(), auditEvent.AfterJson);
        Assert.Contains("New material", auditEvent.AfterJson);
    }
}
