namespace DieCutCatalog.Application.Updates;

public sealed record ClientUpdateManifest(
    string Version,
    string ReleaseName,
    DateTimeOffset PublishedAt,
    string FileName,
    string Sha256,
    long Size,
    string? Notes);

public static class ClientUpdateVersion
{
    public static bool IsNewer(string? candidate, string? current) =>
        TryParse(candidate, out var candidateVersion)
        && TryParse(current, out var currentVersion)
        && candidateVersion > currentVersion;

    private static bool TryParse(string? value, out Version version)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            version = new Version();
            return false;
        }

        var normalized = value.Trim().TrimStart('v', 'V');
        var suffixIndex = normalized.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
        {
            normalized = normalized[..suffixIndex];
        }

        return Version.TryParse(normalized, out version!);
    }
}