namespace DieCutCatalog.Infrastructure.Employees;

public sealed class AccountOptions
{
    public const string SectionName = "Account";
    public int SessionHours { get; set; } = 12;
    public string SetupToken { get; set; } = string.Empty;
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";
    public string RootPath { get; set; } = "storage";
}

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "DieCut Catalog";
    public bool EnableSsl { get; set; } = true;
}
