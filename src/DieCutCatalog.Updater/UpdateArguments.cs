using System.Globalization;

namespace DieCutCatalog.Updater;

internal sealed record UpdateArguments(
    string PackagePath,
    string TargetDirectory,
    string RestartExecutable,
    int ParentProcessId,
    string Version)
{
    public static bool TryParse(string[] args, out UpdateArguments? result, out string error)
    {
        result = null;
        error = "Не указаны параметры обновления.";

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                error = "Параметры обновления имеют неверный формат.";
                return false;
            }

            values[args[index][2..]] = args[index + 1];
        }

        if (!values.TryGetValue("package", out var packagePath)
            || !values.TryGetValue("target", out var targetDirectory)
            || !values.TryGetValue("restart", out var restartExecutable)
            || !values.TryGetValue("parent-pid", out var parentText)
            || !values.TryGetValue("version", out var version)
            || !int.TryParse(parentText, NumberStyles.None, CultureInfo.InvariantCulture, out var parentProcessId)
            || parentProcessId <= 0)
        {
            error = "Не все обязательные параметры обновления указаны корректно.";
            return false;
        }

        result = new UpdateArguments(packagePath, targetDirectory, restartExecutable, parentProcessId, version);
        error = string.Empty;
        return true;
    }
}
