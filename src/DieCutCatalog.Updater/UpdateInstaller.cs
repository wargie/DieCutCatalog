using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace DieCutCatalog.Updater;

internal static class UpdateInstaller
{
    private const string ClientExecutableName = "DieCutCatalog.Desktop.exe";
    private const int MaxArchiveEntries = 10_000;
    private const long MaxExtractedSize = 1024L * 1024 * 1024;

    public static async Task ApplyAsync(UpdateArguments arguments, IProgress<UpdateProgress> progress)
    {
        var packagePath = Path.GetFullPath(arguments.PackagePath);
        var targetDirectory = Path.GetFullPath(arguments.TargetDirectory);
        var restartExecutable = Path.GetFullPath(arguments.RestartExecutable);
        ValidateInputs(packagePath, targetDirectory, restartExecutable);

        progress.Report(new UpdateProgress(5, "Ожидание завершения DieCut Catalog..."));
        await WaitForParentAsync(arguments.ParentProcessId);

        var updateRoot = Path.GetDirectoryName(packagePath)
            ?? throw new InvalidOperationException("Не удалось определить каталог обновления.");
        var stagingDirectory = Path.Combine(updateRoot, "staging");
        var backupDirectory = Path.Combine(updateRoot, "backup");
        RecreateDirectory(stagingDirectory);
        RecreateDirectory(backupDirectory);

        progress.Report(new UpdateProgress(15, "Распаковка и проверка пакета..."));
        ExtractPackage(packagePath, stagingDirectory);
        var payloadDirectory = FindPayloadDirectory(stagingDirectory);

        progress.Report(new UpdateProgress(35, "Создание резервной копии установленной версии..."));
        var installedFiles = new List<InstalledFile>();
        try
        {
            foreach (var sourcePath in Directory.EnumerateFiles(payloadDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(payloadDirectory, sourcePath);
                var targetPath = GetSafeChildPath(targetDirectory, relativePath);
                var backupPath = GetSafeChildPath(backupDirectory, relativePath);
                var existed = File.Exists(targetPath);

                if (existed)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                    File.Copy(targetPath, backupPath, overwrite: true);
                }

                installedFiles.Add(new InstalledFile(targetPath, backupPath, existed));
            }

            progress.Report(new UpdateProgress(55, "Установка новой версии..."));
            for (var index = 0; index < installedFiles.Count; index++)
            {
                var file = installedFiles[index];
                var relativePath = Path.GetRelativePath(targetDirectory, file.TargetPath);
                var sourcePath = GetSafeChildPath(payloadDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(file.TargetPath)!);

                var temporaryTarget = file.TargetPath + ".update-new";
                File.Copy(sourcePath, temporaryTarget, overwrite: true);
                File.Move(temporaryTarget, file.TargetPath, overwrite: true);
                var percentage = 55 + (int)Math.Round((index + 1d) / installedFiles.Count * 40d);
                progress.Report(new UpdateProgress(percentage, $"Установка файлов: {index + 1} из {installedFiles.Count}"));
            }
        }
        catch
        {
            progress.Report(new UpdateProgress(55, "Ошибка установки. Восстановление предыдущей версии..."));
            RollBack(installedFiles);
            throw;
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
        }

        progress.Report(new UpdateProgress(100, "Обновление установлено."));
    }

    public static string WriteErrorLog(Exception exception)
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DieCutCatalog",
            "Logs");
        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(logDirectory, $"update-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        File.WriteAllText(logPath, exception.ToString());
        return logPath;
    }

    private static void ValidateInputs(string packagePath, string targetDirectory, string restartExecutable)
    {
        if (!File.Exists(packagePath) || !string.Equals(Path.GetExtension(packagePath), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Пакет обновления не найден или имеет неверный формат.");
        if (!Directory.Exists(targetDirectory))
            throw new InvalidOperationException("Каталог установленного приложения не найден.");
        if (!IsChildPath(targetDirectory, restartExecutable) || !File.Exists(restartExecutable))
            throw new InvalidOperationException("Путь перезапуска находится вне каталога приложения.");
    }

    private static async Task WaitForParentAsync(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (ArgumentException)
        {
            // The client has already exited.
        }
    }

    private static void ExtractPackage(string packagePath, string stagingDirectory)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        if (archive.Entries.Count > MaxArchiveEntries)
            throw new InvalidOperationException("Пакет обновления содержит слишком много файлов.");

        long extractedSize = 0;
        foreach (var entry in archive.Entries)
        {
            extractedSize = checked(extractedSize + entry.Length);
            if (extractedSize > MaxExtractedSize)
                throw new InvalidOperationException("Распакованный пакет обновления превышает допустимый размер.");

            var destinationPath = GetSafeChildPath(stagingDirectory, entry.FullName);
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static string FindPayloadDirectory(string stagingDirectory)
    {
        if (File.Exists(Path.Combine(stagingDirectory, ClientExecutableName))) return stagingDirectory;

        var candidates = Directory.GetDirectories(stagingDirectory)
            .Where(path => File.Exists(Path.Combine(path, ClientExecutableName)))
            .ToArray();
        return candidates.Length == 1
            ? candidates[0]
            : throw new InvalidOperationException($"Пакет не содержит {ClientExecutableName} в ожидаемом расположении.");
    }

    private static void RollBack(IEnumerable<InstalledFile> installedFiles)
    {
        foreach (var file in installedFiles.Reverse())
        {
            try
            {
                if (file.Existed)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file.TargetPath)!);
                    File.Copy(file.BackupPath, file.TargetPath, overwrite: true);
                }
                else if (File.Exists(file.TargetPath))
                {
                    File.Delete(file.TargetPath);
                }

                var temporaryTarget = file.TargetPath + ".update-new";
                if (File.Exists(temporaryTarget)) File.Delete(temporaryTarget);
            }
            catch
            {
                // Preserve the original installation error; the log contains its details.
            }
        }
    }

    private static string GetSafeChildPath(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Пакет обновления содержит небезопасный путь.");
        return fullPath;
    }

    private static bool IsChildPath(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static void RecreateDirectory(string path)
    {
        TryDeleteDirectory(path);
        Directory.CreateDirectory(path);
    }

    private static void TryDeleteDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private sealed record InstalledFile(string TargetPath, string BackupPath, bool Existed);
}
