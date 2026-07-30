namespace DieCutCatalog.Mobile;

public static class SessionProfile
{
    public static string FirstName { get; set; } = "Adrian";
    public static string LastName { get; set; } = "Test";
    public static string Position { get; set; } = "Администратор";
    public static string Email { get; set; } = "adrian";
    public static string Phone { get; set; } = string.Empty;
    public static string AdditionalContacts { get; set; } = string.Empty;
    public static string Password { get; set; } = string.Empty;

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
