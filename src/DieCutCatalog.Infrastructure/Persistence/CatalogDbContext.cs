using DieCutCatalog.Domain.Auditing;
using DieCutCatalog.Domain.Catalog;
using DieCutCatalog.Domain.Employees;
using Microsoft.EntityFrameworkCore;

namespace DieCutCatalog.Infrastructure.Persistence;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : DbContext(options)
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<EmployeeAccessEvent> EmployeeAccessEvents => Set<EmployeeAccessEvent>();
    public DbSet<Equipment> Equipment => Set<Equipment>();
    public DbSet<CatalogReferenceEntry> CatalogReferenceEntries => Set<CatalogReferenceEntry>();
    public DbSet<ReferenceDirectoryGroup> ReferenceDirectoryGroups => Set<ReferenceDirectoryGroup>();
    public DbSet<ReferenceDirectory> ReferenceDirectories => Set<ReferenceDirectory>();
    public DbSet<ReferenceDirectoryValue> ReferenceDirectoryValues => Set<ReferenceDirectoryValue>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<DieCut> DieCuts => Set<DieCut>();
    public DbSet<DieCutEvent> DieCutEvents => Set<DieCutEvent>();
    public DbSet<DieCutDocument> DieCutDocuments => Set<DieCutDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var employee = modelBuilder.Entity<Employee>();
        employee.ToTable("employees");
        employee.HasKey(x => x.Id);
        employee.HasIndex(x => x.NormalizedEmail).IsUnique();
        employee.Property(x => x.Email).HasMaxLength(320).IsRequired();
        employee.Property(x => x.NormalizedEmail).HasMaxLength(320).IsRequired();
        employee.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
        employee.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
        employee.Property(x => x.LastName).HasMaxLength(100).IsRequired();
        employee.Property(x => x.Position).HasMaxLength(150);
        employee.Property(x => x.Phone).HasMaxLength(50);
        employee.Property(x => x.AdditionalContacts).HasMaxLength(1000);
        employee.Property(x => x.PhotoFileName).HasMaxLength(260);
        employee.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);

        var session = modelBuilder.Entity<UserSession>();
        session.ToTable("user_sessions");
        session.HasKey(x => x.Id);
        session.HasIndex(x => x.TokenHash).IsUnique();
        session.HasIndex(x => new { x.EmployeeId, x.ExpiresAt });
        session.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        session.HasOne(x => x.Employee)
            .WithMany(x => x.Sessions)
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        var accessEvent = modelBuilder.Entity<EmployeeAccessEvent>();
        accessEvent.ToTable("employee_access_events");
        accessEvent.HasKey(x => x.Id);
        accessEvent.HasIndex(x => new { x.EmployeeId, x.OccurredAt });
        accessEvent.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
        accessEvent.HasOne(x => x.Employee)
            .WithMany(x => x.AccessEvents)
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        var equipment = modelBuilder.Entity<Equipment>();
        equipment.ToTable("equipment");
        equipment.HasKey(x => x.Id);
        equipment.HasIndex(x => x.NormalizedName).IsUnique();
        equipment.Property(x => x.Name).HasMaxLength(150).IsRequired();
        equipment.Property(x => x.NormalizedName).HasMaxLength(150).IsRequired();
        equipment.Property(x => x.ArticleRtf).HasColumnType("text");

        var reference = modelBuilder.Entity<CatalogReferenceEntry>();
        reference.ToTable("catalog_reference_entries");
        reference.HasKey(x => x.Id);
        reference.HasIndex(x => new { x.Kind, x.NormalizedName }).IsUnique();
        reference.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
        reference.Property(x => x.Name).HasMaxLength(200).IsRequired();
        reference.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired();
        reference.Property(x => x.ArticleRtf).HasColumnType("text");

        var directoryGroup = modelBuilder.Entity<ReferenceDirectoryGroup>();
        directoryGroup.ToTable("reference_directory_groups");
        directoryGroup.HasKey(x => x.Id);
        directoryGroup.HasIndex(x => x.NormalizedName).IsUnique();
        directoryGroup.Property(x => x.Name).HasMaxLength(120).IsRequired();
        directoryGroup.Property(x => x.NormalizedName).HasMaxLength(120).IsRequired();

        var directory = modelBuilder.Entity<ReferenceDirectory>();
        directory.ToTable("reference_directories");
        directory.HasKey(x => x.Id);
        directory.HasIndex(x => x.NormalizedName).IsUnique();
        directory.Property(x => x.Name).HasMaxLength(120).IsRequired();
        directory.Property(x => x.NormalizedName).HasMaxLength(120).IsRequired();
        directory.Property(x => x.Description).HasMaxLength(500);
        directory.HasOne(x => x.Group).WithMany(x => x.Directories).HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.SetNull);

        var directoryValue = modelBuilder.Entity<ReferenceDirectoryValue>();
        directoryValue.ToTable("reference_directory_values");
        directoryValue.HasKey(x => x.Id);
        directoryValue.HasIndex(x => new { x.DirectoryId, x.NormalizedName }).IsUnique();
        directoryValue.Property(x => x.Name).HasMaxLength(200).IsRequired();
        directoryValue.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired();
        directoryValue.Property(x => x.ArticleRtf).HasColumnType("text");
        directoryValue.HasOne(x => x.Directory).WithMany(x => x.Values).HasForeignKey(x => x.DirectoryId)
            .OnDelete(DeleteBehavior.Cascade);

        var auditEvent = modelBuilder.Entity<AuditEvent>();
        auditEvent.ToTable("audit_events");
        auditEvent.HasKey(x => x.Id);
        auditEvent.HasIndex(x => x.OccurredAt);
        auditEvent.HasIndex(x => new { x.ActorEmployeeId, x.OccurredAt });
        auditEvent.HasIndex(x => x.CorrelationId);
        auditEvent.Property(x => x.EntityType).HasConversion<string>().HasMaxLength(64);
        auditEvent.Property(x => x.Action).HasConversion<string>().HasMaxLength(64);
        auditEvent.Property(x => x.BeforeJson).HasColumnType("jsonb");
        auditEvent.Property(x => x.AfterJson).HasColumnType("jsonb");
        auditEvent.HasOne(x => x.ActorEmployee)
            .WithMany()
            .HasForeignKey(x => x.ActorEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        auditEvent.HasOne(x => x.ApproverEmployee)
            .WithMany()
            .HasForeignKey(x => x.ApproverEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        var dieCut = modelBuilder.Entity<DieCut>();
        dieCut.ToTable("die_cuts");
        dieCut.HasKey(x => x.Id);
        dieCut.HasIndex(x => new { x.EquipmentId, x.NormalizedNumber }).IsUnique().HasFilter("\"Status\" <> 'Deleted'");
        dieCut.HasIndex(x => x.Status);
        dieCut.HasIndex(x => x.Material);
        dieCut.Property(x => x.Number).HasMaxLength(50).IsRequired();
        dieCut.Property(x => x.NormalizedNumber).HasMaxLength(50).IsRequired();
        dieCut.Property(x => x.JcOrderNumber).HasMaxLength(100);
        dieCut.Property(x => x.X).HasPrecision(10, 3);
        dieCut.Property(x => x.Y).HasPrecision(10, 3);
        dieCut.Property(x => x.GrooveSpacing).HasPrecision(10, 3);
        dieCut.Property(x => x.LabelCornerRadius).HasPrecision(10, 3);
        dieCut.Property(x => x.GapX).HasPrecision(14, 9);
        dieCut.Property(x => x.GapY).HasPrecision(14, 9);
        dieCut.Property(x => x.Material).HasMaxLength(200).IsRequired();
        dieCut.Property(x => x.H).HasPrecision(10, 2);
        dieCut.Property(x => x.RunLengthMeters).HasPrecision(18, 6);
        dieCut.Property(x => x.LifetimeRunLengthMeters).HasPrecision(18, 6);
        dieCut.Property(x => x.Figure).HasMaxLength(100).IsRequired();
        dieCut.Property(x => x.Comments).HasMaxLength(2000);
        dieCut.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        dieCut.Property(x => x.JustCutPriceAmount).HasPrecision(18, 2);
        dieCut.Property(x => x.JustCutPriceCurrency).HasMaxLength(16);
        dieCut.Property(x => x.JustCutEnvironment).HasMaxLength(32);
        dieCut.HasOne(x => x.Equipment)
            .WithMany(x => x.DieCuts)
            .HasForeignKey(x => x.EquipmentId)
            .OnDelete(DeleteBehavior.Restrict);

        var dieCutEvent = modelBuilder.Entity<DieCutEvent>();
        dieCutEvent.ToTable("die_cut_events");
        dieCutEvent.HasKey(x => x.Id);
        dieCutEvent.HasIndex(x => new { x.DieCutId, x.OccurredAt });
        dieCutEvent.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
        dieCutEvent.Property(x => x.RunLengthMetersBefore).HasPrecision(18, 6);
        dieCutEvent.Property(x => x.RunLengthMetersAfter).HasPrecision(18, 6);
        dieCutEvent.Property(x => x.JustCutPriceAmount).HasPrecision(18, 2);
        dieCutEvent.Property(x => x.JustCutPriceCurrency).HasMaxLength(16);
        dieCutEvent.Property(x => x.JustCutEnvironment).HasMaxLength(32);
        dieCutEvent.HasOne(x => x.DieCut)
            .WithMany(x => x.Events)
            .HasForeignKey(x => x.DieCutId)
            .OnDelete(DeleteBehavior.Cascade);
        dieCutEvent.HasOne(x => x.Employee)
            .WithMany()
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        var dieCutDocument = modelBuilder.Entity<DieCutDocument>();
        dieCutDocument.ToTable("die_cut_documents");
        dieCutDocument.HasKey(x => x.Id);
        dieCutDocument.HasIndex(x => new { x.DieCutId, x.CreatedAt });
        dieCutDocument.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
        dieCutDocument.Property(x => x.StoragePath).HasMaxLength(500).IsRequired();
        dieCutDocument.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        dieCutDocument.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        dieCutDocument.Property(x => x.Source).HasConversion<string>().HasMaxLength(32);
        dieCutDocument.HasOne(x => x.DieCut)
            .WithMany(x => x.Documents)
            .HasForeignKey(x => x.DieCutId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
