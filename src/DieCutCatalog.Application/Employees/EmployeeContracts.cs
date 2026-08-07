using DieCutCatalog.Domain.Catalog;
using DieCutCatalog.Domain.Employees;

namespace DieCutCatalog.Application.Employees;

public sealed record CreateEmployeeCommand(
    string Email,
    string FirstName,
    string LastName,
    string? Position,
    string? Phone,
    EmployeeRole Role);

public sealed record LoginCommand(string Email, string Password);

public sealed record LoginResult(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    bool MustChangePassword,
    EmployeeProfile Profile);

public sealed record EmployeeProfile(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? Position,
    string? Phone,
    string? AdditionalContacts,
    string? PhotoUrl,
    EmployeeRole Role,
    bool MustChangePassword,
    bool IsActive);

public sealed record EmployeeActivityEntry(
    Guid Id,
    Guid DieCutId,
    string DieCutNumber,
    string Equipment,
    DieCutEventType Type,
    long? Quantity,
    long MileageAfter,
    decimal RunLengthMetersAfter,
    long RevolutionsAfter,
    DateTimeOffset OccurredAt);

public sealed record EmployeeAccessActivityEntry(
    Guid Id,
    EmployeeAccessEventType Type,
    DateTimeOffset OccurredAt);

public sealed record EmployeeActivityReport(
    EmployeeProfile Employee,
    IReadOnlyList<EmployeeActivityEntry> Activities,
    int KnivesCount,
    int CreatedCount,
    int DeletedCount,
    long TotalCirculation,
    IReadOnlyList<EmployeeAccessActivityEntry> AccessActivities);
public sealed record CreateEmployeeResult(EmployeeProfile Profile, bool EmailDelivered, string? TemporaryPassword);

public sealed record UpdateEmployeeProfileCommand(
    string FirstName,
    string LastName,
    string? Position,
    string? Phone,
    string? AdditionalContacts);

public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword);

public sealed record ChangeEmailCommand(string CurrentPassword, string NewEmail);

public sealed record StoredPhoto(string FileName, string ContentType, Stream Content, long Length);

public interface IAccountService
{
    Task<IReadOnlyList<EmployeeActivityReport>> GetEmployeeDirectoryAsync(CancellationToken cancellationToken = default);

    Task<EmployeeProfile?> DeactivateEmployeeAsync(Guid employeeId, Guid requestingEmployeeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EmployeeProfile>> GetEmployeesAsync(CancellationToken cancellationToken = default);

    Task<EmployeeActivityReport?> GetEmployeeActivityAsync(Guid employeeId, CancellationToken cancellationToken = default);

    Task<CreateEmployeeResult> CreateEmployeeAsync(
        CreateEmployeeCommand command,
        CancellationToken cancellationToken = default);

    Task<EmployeeProfile> CreateInitialAdministratorAsync(
        CreateEmployeeCommand command,
        string setupToken,
        CancellationToken cancellationToken = default);

    Task<LoginResult?> LoginAsync(
        LoginCommand command,
        CancellationToken cancellationToken = default);

    Task<bool> LogoutAsync(
        string accessToken,
        CancellationToken cancellationToken = default);

    Task<EmployeeProfile?> ResumeSessionAsync(
        string accessToken,
        CancellationToken cancellationToken = default);

    Task<bool> DisconnectSessionAsync(
        string accessToken,
        CancellationToken cancellationToken = default);

    Task<EmployeeProfile?> GetProfileAsync(
        string accessToken,
        CancellationToken cancellationToken = default);

    Task<bool> VerifyPasswordAsync(
        string accessToken,
        string password,
        CancellationToken cancellationToken = default);
    Task<EmployeeProfile?> VerifyAdministratorPasswordAsync(
        string password,
        CancellationToken cancellationToken = default);

    Task<EmployeeProfile?> UpdateProfileAsync(
        string accessToken,
        UpdateEmployeeProfileCommand command,
        CancellationToken cancellationToken = default);

    Task<bool> ChangePasswordAsync(
        string accessToken,
        ChangePasswordCommand command,
        CancellationToken cancellationToken = default);

    Task<bool> ChangeEmailAsync(
        string accessToken,
        ChangeEmailCommand command,
        CancellationToken cancellationToken = default);

    Task<EmployeeProfile?> SavePhotoAsync(
        string accessToken,
        StoredPhoto photo,
        CancellationToken cancellationToken = default);

    Task<bool> IsAdministratorAsync(
        string accessToken,
        CancellationToken cancellationToken = default);
}

public interface IAccountEmailSender
{
    Task SendTemporaryPasswordAsync(
        string recipientEmail,
        string employeeName,
        string temporaryPassword,
        CancellationToken cancellationToken = default);
}
