namespace DieCutCatalog.Mobile.Models;

public sealed record AndroidUpdateManifest(
    string Version,
    int VersionCode,
    bool Required,
    string ReleaseName,
    DateTimeOffset PublishedAt,
    string FileName,
    string Sha256,
    long Size,
    string? Notes);

public sealed record UpdateDownloadProgress(long Received, long Total)
{
    public double Fraction => Total <= 0 ? 0 : Math.Clamp((double)Received / Total, 0, 1);
}
