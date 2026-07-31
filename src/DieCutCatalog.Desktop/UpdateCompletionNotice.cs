using System.IO;

namespace DieCutCatalog.Desktop;

internal static class UpdateCompletionNotice
{
    private const string MarkerFileName = "update-completed.txt";

    public static string? TryTake()
    {
        var markerPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DieCutCatalog",
            "Updates",
            MarkerFileName);

        try
        {
            if (!File.Exists(markerPath)) return null;
            var version = File.ReadAllText(markerPath).Trim();
            File.Delete(markerPath);
            return string.IsNullOrWhiteSpace(version) || version.Length > 64 ? null : version;
        }
        catch
        {
            return null;
        }
    }
}
