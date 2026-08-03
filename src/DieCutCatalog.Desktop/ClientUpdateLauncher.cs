using System.Diagnostics;
using System.Globalization;
using System.IO;
using DieCutCatalog.Application.Updates;

namespace DieCutCatalog.Desktop;

internal static class ClientUpdateLauncher
{
    private const string UpdaterExecutableName = "DieCutCatalog.Updater.exe";

    public static string PreparePackagePath(ClientUpdateManifest manifest)
    {
        var version = SanitizeSegment(manifest.Version);
        var updateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DieCutCatalog",
            "Updates",
            version,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(updateDirectory);
        return Path.Combine(updateDirectory, manifest.FileName);
    }

    public static void DiscardPackage(string packagePath)
    {
        try
        {
            var updateRoot = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DieCutCatalog",
                "Updates"));
            var packageDirectory = Path.GetFullPath(Path.GetDirectoryName(packagePath)!);
            var rootPrefix = updateRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (packageDirectory.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(packageDirectory))
            {
                Directory.Delete(packageDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A failed attempt is isolated and can be removed by a later maintenance pass.
        }
        catch (UnauthorizedAccessException)
        {
            // Cleanup must not replace the update error shown to the user.
        }
    }
    public static void Start(ClientUpdateManifest manifest, string packagePath)
    {
        var applicationDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var applicationExecutable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Не удалось определить путь запущенного приложения.");
        var installedUpdater = Path.Combine(applicationDirectory, UpdaterExecutableName);
        if (!File.Exists(installedUpdater))
        {
            throw new CatalogApiException(
                "В установленной версии ещё нет автоматического установщика. " +
                "Это переходное обновление нужно установить из ZIP вручную один раз; последующие версии будут обновляться автоматически.");
        }

        var runnerDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DieCutCatalog",
            "Updater",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runnerDirectory);
        var runnerPath = Path.Combine(runnerDirectory, UpdaterExecutableName);
        File.Copy(installedUpdater, runnerPath, overwrite: true);

        var startInfo = new ProcessStartInfo(runnerPath)
        {
            UseShellExecute = true,
            WorkingDirectory = runnerDirectory
        };
        startInfo.ArgumentList.Add("--package");
        startInfo.ArgumentList.Add(Path.GetFullPath(packagePath));
        startInfo.ArgumentList.Add("--target");
        startInfo.ArgumentList.Add(applicationDirectory);
        startInfo.ArgumentList.Add("--restart");
        startInfo.ArgumentList.Add(Path.GetFullPath(applicationExecutable));
        startInfo.ArgumentList.Add("--parent-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--version");
        startInfo.ArgumentList.Add(manifest.Version);

        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Не удалось запустить установщик обновления.");
    }

    private static string SanitizeSegment(string value)
    {
        var sanitized = string.Concat(value.Trim().Where(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_'));
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }
}
