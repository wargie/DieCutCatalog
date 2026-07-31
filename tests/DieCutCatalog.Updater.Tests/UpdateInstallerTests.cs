using System.IO.Compression;
using DieCutCatalog.Updater;

namespace DieCutCatalog.Updater.Tests;

public sealed class UpdateInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DieCutCatalogUpdaterTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Apply_ReplacesFilesAndKeepsBackup()
    {
        var target = Path.Combine(_root, "client");
        Directory.CreateDirectory(target);
        var executable = Path.Combine(target, "DieCutCatalog.Desktop.exe");
        await File.WriteAllTextAsync(executable, "old-exe");
        await File.WriteAllTextAsync(Path.Combine(target, "settings.dll"), "old-settings");

        var package = CreatePackage(("DieCutCatalog.Desktop.exe", "new-exe"), ("settings.dll", "new-settings"));
        var arguments = new UpdateArguments(package, target, executable, int.MaxValue, "9.9.9");

        await UpdateInstaller.ApplyAsync(arguments, new Progress<string>());

        Assert.Equal("new-exe", await File.ReadAllTextAsync(executable));
        Assert.Equal("new-settings", await File.ReadAllTextAsync(Path.Combine(target, "settings.dll")));
        Assert.Equal("old-exe", await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(package)!, "backup", "DieCutCatalog.Desktop.exe")));
    }

    [Fact]
    public async Task Apply_RejectsPathTraversal()
    {
        var target = Path.Combine(_root, "client");
        Directory.CreateDirectory(target);
        var executable = Path.Combine(target, "DieCutCatalog.Desktop.exe");
        await File.WriteAllTextAsync(executable, "old-exe");

        var packageDirectory = Path.Combine(_root, "package");
        Directory.CreateDirectory(packageDirectory);
        var package = Path.Combine(packageDirectory, "update.zip");
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            var executableEntry = archive.CreateEntry("DieCutCatalog.Desktop.exe");
            await using (var stream = executableEntry.Open())
            await using (var writer = new StreamWriter(stream)) await writer.WriteAsync("new-exe");
            archive.CreateEntry("../outside.txt");
        }

        var arguments = new UpdateArguments(package, target, executable, int.MaxValue, "9.9.9");
        await Assert.ThrowsAsync<InvalidOperationException>(() => UpdateInstaller.ApplyAsync(arguments, new Progress<string>()));
        Assert.False(File.Exists(Path.Combine(packageDirectory, "outside.txt")));
        Assert.Equal("old-exe", await File.ReadAllTextAsync(executable));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string CreatePackage(params (string Name, string Content)[] files)
    {
        var packageDirectory = Path.Combine(_root, "package");
        Directory.CreateDirectory(packageDirectory);
        var package = Path.Combine(packageDirectory, "update.zip");
        using var archive = ZipFile.Open(package, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            var entry = archive.CreateEntry(file.Name);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream);
            writer.Write(file.Content);
        }
        return package;
    }
}
