using DieCutCatalog.Mobile.Models;

namespace DieCutCatalog.Mobile;

public static class SessionProfile
{
    public static string FirstName { get; private set; } = string.Empty;
    public static string LastName { get; private set; } = string.Empty;
    public static string Position { get; private set; } = string.Empty;
    public static string Email { get; private set; } = string.Empty;
    public static string Phone { get; private set; } = string.Empty;
    public static string AdditionalContacts { get; private set; } = string.Empty;

    public static void Apply(EmployeeProfileDto profile)
    {
        FirstName = profile.FirstName;
        LastName = profile.LastName;
        Position = profile.Position ?? string.Empty;
        Email = profile.Email;
        Phone = profile.Phone ?? string.Empty;
        AdditionalContacts = profile.AdditionalContacts ?? string.Empty;
    }

    public static string Initials
    {
        get
        {
            var first = FirstName.FirstOrDefault();
            var last = LastName.FirstOrDefault();
            var initials = string.Concat(first == default ? null : first, last == default ? null : last);
            return string.IsNullOrWhiteSpace(initials) ? "?" : initials.ToUpperInvariant();
        }
    }
}
