namespace DieCutCatalog.Domain.Employees;

public sealed class Employee
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool MustChangePassword { get; set; } = true;
    public EmployeeRole Role { get; set; } = EmployeeRole.Operator;
    public bool IsActive { get; set; } = true;

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Position { get; set; }
    public string? Phone { get; set; }
    public string? AdditionalContacts { get; set; }
    public string? PhotoFileName { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<UserSession> Sessions { get; set; } = new List<UserSession>();
    public ICollection<EmployeeAccessEvent> AccessEvents { get; set; } = new List<EmployeeAccessEvent>();
}
