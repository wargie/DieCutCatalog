namespace DieCutCatalog.Application.Updates;

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
