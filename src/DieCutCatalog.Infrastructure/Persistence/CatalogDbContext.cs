using DieCutCatalog.Domain.Catalog;
using DieCutCatalog.Domain.Employees;
using Microsoft.EntityFrameworkCore;

namespace DieCutCatalog.Infrastructure.Persistence;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : DbContext(options)
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<Equipment> Equipment => Set<Equipment>();
    public DbSet<DieCut> DieCuts => Set<DieCut>();

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

        var equipment = modelBuilder.Entity<Equipment>();
        equipment.ToTable("equipment");
        equipment.HasKey(x => x.Id);
        equipment.HasIndex(x => x.NormalizedName).IsUnique();
        equipment.Property(x => x.Name).HasMaxLength(150).IsRequired();
        equipment.Property(x => x.NormalizedName).HasMaxLength(150).IsRequired();

        var dieCut = modelBuilder.Entity<DieCut>();
        dieCut.ToTable("die_cuts");
        dieCut.HasKey(x => x.Id);
        dieCut.HasIndex(x => new { x.EquipmentId, x.NormalizedNumber }).IsUnique();
        dieCut.HasIndex(x => x.Status);
        dieCut.HasIndex(x => x.Material);
        dieCut.Property(x => x.Number).HasMaxLength(50).IsRequired();
        dieCut.Property(x => x.NormalizedNumber).HasMaxLength(50).IsRequired();


        dieCut.Property(x => x.X).HasPrecision(10, 3);
        dieCut.Property(x => x.Y).HasPrecision(10, 3);
        dieCut.Property(x => x.GapX).HasPrecision(14, 9);
        dieCut.Property(x => x.GapY).HasPrecision(14, 9);
        dieCut.Property(x => x.Material).HasMaxLength(200).IsRequired();
        dieCut.Property(x => x.H).HasPrecision(10, 2);
        dieCut.Property(x => x.Figure).HasMaxLength(100).IsRequired();
        dieCut.Property(x => x.Comments).HasMaxLength(2000);
        dieCut.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        dieCut.HasOne(x => x.Equipment)
            .WithMany(x => x.DieCuts)
            .HasForeignKey(x => x.EquipmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
