using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using DieCutCatalog.Application.Employees;
using DieCutCatalog.Domain.Catalog;
using DieCutCatalog.Domain.Employees;
using DieCutCatalog.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DieCutCatalog.Infrastructure.Employees;

public sealed class AccountService(
    CatalogDbContext dbContext,
    IPasswordHasher<Employee> passwordHasher,
    IAccountEmailSender emailSender,
    IOptions<AccountOptions> accountOptions,
    IOptions<StorageOptions> storageOptions)
    : IAccountService
{
    private static readonly EmailAddressAttribute EmailValidator = new();
    private readonly AccountOptions _accountOptions = accountOptions.Value;
    private readonly StorageOptions _storageOptions = storageOptions.Value;

    public async Task<IReadOnlyList<EmployeeProfile>> GetEmployeesAsync(
        CancellationToken cancellationToken = default)
    {
        var employees = await dbContext.Employees.AsNoTracking()
            .OrderBy(x => x.LastName)
            .ThenBy(x => x.FirstName)
            .ToListAsync(cancellationToken);
        return employees.Select(ToProfile).ToArray();
    }

    public async Task<EmployeeActivityReport?> GetEmployeeActivityAsync(
        Guid employeeId, CancellationToken cancellationToken = default)
    {
        var employee = await dbContext.Employees.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
        if (employee is null) return null;

        var activities = await dbContext.DieCutEvents.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.OccurredAt)
            .Select(x => new EmployeeActivityEntry(
                x.Id, x.DieCutId, x.DieCut.Number, x.DieCut.Equipment.Name, x.Type,
                x.Quantity, x.MileageAfter, x.RunLengthMetersAfter, x.RevolutionsAfter, x.OccurredAt))
            .ToListAsync(cancellationToken);

        return new EmployeeActivityReport(
            ToProfile(employee),
            activities,
            activities.Select(x => x.DieCutId).Distinct().Count(),
            activities.Count(x => x.Type == DieCutEventType.Created),
            activities.Count(x => x.Type == DieCutEventType.Deleted),
            activities.Where(x => x.Type == DieCutEventType.CirculationAdded).Sum(x => x.Quantity ?? 0));
    }
    public async Task<CreateEmployeeResult> CreateEmployeeAsync(
        CreateEmployeeCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateCreateCommand(command);
        return await CreateEmployeeCoreAsync(command, true, cancellationToken);
    }

    public async Task<EmployeeProfile> CreateInitialAdministratorAsync(
        CreateEmployeeCommand command,
        string setupToken,
        CancellationToken cancellationToken = default)
    {
        if (!SecureEquals(setupToken, _accountOptions.SetupToken)
            || string.IsNullOrWhiteSpace(_accountOptions.SetupToken))
        {
            throw new InvalidSetupTokenException();
        }

        if (await dbContext.Employees.AnyAsync(cancellationToken))
        {
            throw new SetupAlreadyCompletedException();
        }

        ValidateCreateCommand(command);
        return (await CreateEmployeeCoreAsync(
            command with { Role = EmployeeRole.Administrator },
            false,
            cancellationToken)).Profile;
    }

    public async Task<LoginResult?> LoginAsync(
        LoginCommand command,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(command.Email);
        var employee = await dbContext.Employees
            .SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);

        if (employee is null || !employee.IsActive)
        {
            return null;
        }

        var result = passwordHasher.VerifyHashedPassword(
            employee,
            employee.PasswordHash,
            command.Password);

        if (result == PasswordVerificationResult.Failed)
        {
            return null;
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            employee.PasswordHash = passwordHasher.HashPassword(employee, command.Password);
        }

        var rawToken = GenerateToken();
        var expiresAt = DateTimeOffset.UtcNow.AddHours(
            Math.Clamp(_accountOptions.SessionHours, 1, 168));

        dbContext.UserSessions.Add(new UserSession
        {
            EmployeeId = employee.Id,
            TokenHash = HashToken(rawToken),
            ExpiresAt = expiresAt
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return new LoginResult(
            rawToken,
            expiresAt,
            employee.MustChangePassword,
            ToProfile(employee));
    }

    public async Task<bool> LogoutAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return false;
        }

        var tokenHash = HashToken(accessToken);
        var session = await dbContext.UserSessions.SingleOrDefaultAsync(
            x => x.TokenHash == tokenHash && x.RevokedAt == null,
            cancellationToken);
        if (session is null)
        {
            return false;
        }

        session.RevokedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
    public async Task<EmployeeProfile?> GetProfileAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var employee = await FindEmployeeByTokenAsync(accessToken, cancellationToken);
        return employee is null ? null : ToProfile(employee);
    }

    public async Task<bool> VerifyPasswordAsync(
        string accessToken,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(password)) return false;
        var employee = await FindEmployeeByTokenAsync(accessToken, cancellationToken);
        if (employee is null) return false;

        var result = passwordHasher.VerifyHashedPassword(employee, employee.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed) return false;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            employee.PasswordHash = passwordHasher.HashPassword(employee, password);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<EmployeeProfile?> VerifyAdministratorPasswordAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(password)) return null;

        var administrators = await dbContext.Employees
            .Where(x => x.IsActive
                && !x.MustChangePassword
                && x.Role == EmployeeRole.Administrator)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var administrator in administrators)
        {
            var result = passwordHasher.VerifyHashedPassword(
                administrator,
                administrator.PasswordHash,
                password);
            if (result == PasswordVerificationResult.Failed) continue;

            if (result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                administrator.PasswordHash = passwordHasher.HashPassword(administrator, password);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return ToProfile(administrator);
        }

        return null;
    }
    public async Task<EmployeeProfile?> UpdateProfileAsync(
        string accessToken,
        UpdateEmployeeProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateRequiredName(command.FirstName, nameof(command.FirstName));
        ValidateRequiredName(command.LastName, nameof(command.LastName));

        var employee = await FindEmployeeByTokenAsync(accessToken, cancellationToken);
        if (employee is null)
        {
            return null;
        }

        employee.FirstName = command.FirstName.Trim();
        employee.LastName = command.LastName.Trim();
        employee.Position = TrimToNull(command.Position);
        employee.Phone = TrimToNull(command.Phone);
        employee.AdditionalContacts = TrimToNull(command.AdditionalContacts);
        employee.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToProfile(employee);
    }

    public async Task<bool> ChangePasswordAsync(
        string accessToken,
        ChangePasswordCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateNewPassword(command.NewPassword);

        var tokenHash = HashToken(accessToken);
        var session = await dbContext.UserSessions
            .Include(x => x.Employee)
            .SingleOrDefaultAsync(
                x => x.TokenHash == tokenHash
                    && x.RevokedAt == null
                    && x.ExpiresAt > DateTimeOffset.UtcNow
                    && x.Employee.IsActive,
                cancellationToken);

        if (session is null)
        {
            return false;
        }

        var employee = session.Employee;
        if (passwordHasher.VerifyHashedPassword(
                employee,
                employee.PasswordHash,
                command.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            return false;
        }

        employee.PasswordHash = passwordHasher.HashPassword(employee, command.NewPassword);
        employee.MustChangePassword = false;
        employee.UpdatedAt = DateTimeOffset.UtcNow;

        var now = DateTimeOffset.UtcNow;
        var otherSessions = await dbContext.UserSessions
            .Where(x => x.EmployeeId == employee.Id && x.Id != session.Id && x.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var otherSession in otherSessions)
        {
            otherSession.RevokedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ChangeEmailAsync(
        string accessToken,
        ChangeEmailCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateEmail(command.NewEmail);

        var employee = await FindEmployeeByTokenAsync(accessToken, cancellationToken);
        if (employee is null)
        {
            return false;
        }

        if (passwordHasher.VerifyHashedPassword(
                employee,
                employee.PasswordHash,
                command.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            return false;
        }

        var normalizedEmail = NormalizeEmail(command.NewEmail);
        if (await dbContext.Employees.AnyAsync(
                x => x.Id != employee.Id && x.NormalizedEmail == normalizedEmail,
                cancellationToken))
        {
            throw new DuplicateEmailException(command.NewEmail);
        }

        employee.Email = command.NewEmail.Trim();
        employee.NormalizedEmail = normalizedEmail;
        employee.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<EmployeeProfile?> SavePhotoAsync(
        string accessToken,
        StoredPhoto photo,
        CancellationToken cancellationToken = default)
    {
        if (photo.Length <= 0 || photo.Length > 5 * 1024 * 1024)
        {
            throw new ValidationException("Photo size must be between 1 byte and 5 MB.");
        }

        var extension = photo.ContentType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => throw new ValidationException("Only JPEG, PNG and WebP photos are supported.")
        };

        var employee = await FindEmployeeByTokenAsync(accessToken, cancellationToken);
        if (employee is null)
        {
            return null;
        }

        var relativePath = Path.Combine("employees", employee.Id.ToString("N"), "profile" + extension);
        var fullPath = GetStoragePath(relativePath);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);

        var temporaryPath = fullPath + ".tmp";
        await using (var target = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous))
        {
            await photo.Content.CopyToAsync(target, cancellationToken);
        }

        if (!HasExpectedImageSignature(temporaryPath, extension))
        {
            File.Delete(temporaryPath);
            throw new ValidationException("Photo content does not match its image type.");
        }

        File.Move(temporaryPath, fullPath, true);

        if (!string.IsNullOrWhiteSpace(employee.PhotoFileName)
            && !string.Equals(employee.PhotoFileName, relativePath, StringComparison.Ordinal))
        {
            var previousPath = GetStoragePath(employee.PhotoFileName);
            if (File.Exists(previousPath))
            {
                File.Delete(previousPath);
            }
        }

        employee.PhotoFileName = relativePath;
        employee.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToProfile(employee);
    }

    public async Task<bool> IsAdministratorAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var employee = await FindEmployeeByTokenAsync(accessToken, cancellationToken);
        return employee is { Role: EmployeeRole.Administrator, MustChangePassword: false };
    }

    private async Task<CreateEmployeeResult> CreateEmployeeCoreAsync(
        CreateEmployeeCommand command,
        bool returnPasswordOnEmailFailure,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(command.Email);
        if (await dbContext.Employees.AnyAsync(
                x => x.NormalizedEmail == normalizedEmail,
                cancellationToken))
        {
            throw new DuplicateEmailException(command.Email);
        }

        var temporaryPassword = GenerateTemporaryPassword();
        var employee = new Employee
        {
            Email = command.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            FirstName = command.FirstName.Trim(),
            LastName = command.LastName.Trim(),
            Position = TrimToNull(command.Position),
            Phone = TrimToNull(command.Phone),
            Role = command.Role,
            MustChangePassword = true
        };
        employee.PasswordHash = passwordHasher.HashPassword(employee, temporaryPassword);

        dbContext.Employees.Add(employee);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await emailSender.SendTemporaryPasswordAsync(
                employee.Email,
                $"{employee.FirstName} {employee.LastName}".Trim(),
                temporaryPassword,
                cancellationToken);
            return new CreateEmployeeResult(ToProfile(employee), true, null);
        }
        catch (EmailDeliveryUnavailableException) when (returnPasswordOnEmailFailure)
        {
            return new CreateEmployeeResult(ToProfile(employee), false, temporaryPassword);
        }
        catch
        {
            dbContext.Employees.Remove(employee);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<Employee?> FindEmployeeByTokenAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        var tokenHash = HashToken(accessToken);
        return await dbContext.UserSessions
            .Where(x => x.TokenHash == tokenHash
                && x.RevokedAt == null
                && x.ExpiresAt > DateTimeOffset.UtcNow
                && x.Employee.IsActive)
            .Select(x => x.Employee)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private string GetStoragePath(string relativePath)
    {
        var storageRoot = Path.GetFullPath(_storageOptions.RootPath);
        var path = Path.GetFullPath(Path.Combine(storageRoot, relativePath));
        if (!path.StartsWith(storageRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid storage path.");
        }

        return path;
    }

    private static bool HasExpectedImageSignature(string path, string extension)
    {
        Span<byte> header = stackalloc byte[12];
        using var stream = File.OpenRead(path);
        var bytesRead = stream.Read(header);

        return extension switch
        {
            ".jpg" => bytesRead >= 3
                && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            ".png" => bytesRead >= 8
                && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            ".webp" => bytesRead >= 12
                && header[..4].SequenceEqual("RIFF"u8)
                && header[8..12].SequenceEqual("WEBP"u8),
            _ => false
        };
    }
    private static EmployeeProfile ToProfile(Employee employee) => new(
        employee.Id,
        employee.Email,
        employee.FirstName,
        employee.LastName,
        employee.Position,
        employee.Phone,
        employee.AdditionalContacts,
        employee.PhotoFileName is null ? null : $"/api/employees/{employee.Id}/photo",
        employee.Role,
        employee.MustChangePassword,
        employee.IsActive);

    private static void ValidateCreateCommand(CreateEmployeeCommand command)
    {
        ValidateEmail(command.Email);
        ValidateRequiredName(command.FirstName, nameof(command.FirstName));
        ValidateRequiredName(command.LastName, nameof(command.LastName));
    }

    private static void ValidateRequiredName(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 100)
        {
            throw new ValidationException($"{fieldName} is required and must not exceed 100 characters.");
        }
    }

    private static void ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)
            || email.Length > 320
            || !EmailValidator.IsValid(email.Trim()))
        {
            throw new ValidationException("A valid email address is required.");
        }
    }

    private static void ValidateNewPassword(string password)
    {
        if (password.Length < 12
            || !password.Any(char.IsUpper)
            || !password.Any(char.IsLower)
            || !password.Any(char.IsDigit)
            || !password.Any(ch => !char.IsLetterOrDigit(ch)))
        {
            throw new ValidationException(
                "Password must contain at least 12 characters, upper and lower case letters, a digit and a special character.");
        }
    }

    private static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string GenerateTemporaryPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "!@$%*-_";
        const string all = upper + lower + digits + symbols;

        var characters = new char[18];
        characters[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
        characters[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
        characters[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        characters[3] = symbols[RandomNumberGenerator.GetInt32(symbols.Length)];

        for (var index = 4; index < characters.Length; index++)
        {
            characters[index] = all[RandomNumberGenerator.GetInt32(all.Length)];
        }

        RandomNumberGenerator.Shuffle<char>(characters);
        return new string(characters);
    }

    private static string GenerateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)))
            .ToLowerInvariant();

    private static bool SecureEquals(string left, string right)
    {
        var leftHash = SHA256.HashData(Encoding.UTF8.GetBytes(left ?? string.Empty));
        var rightHash = SHA256.HashData(Encoding.UTF8.GetBytes(right ?? string.Empty));
        return CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
    }
}
