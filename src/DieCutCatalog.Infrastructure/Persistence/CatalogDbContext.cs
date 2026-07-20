using DieCutCatalog.Domain.Employees;
using Microsoft.EntityFrameworkCore;

namespace DieCutCatalog.Infrastructure.Persistence;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : DbContext(options)
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();

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
    }
}
