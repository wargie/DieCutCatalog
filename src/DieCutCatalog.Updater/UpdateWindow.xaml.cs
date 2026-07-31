using System.Diagnostics;
using System.IO;
using System.Windows;

namespace DieCutCatalog.Updater;

public partial class UpdateWindow : Window
{
    private readonly UpdateArguments _arguments;

    internal UpdateWindow(UpdateArguments arguments)
    {
        _arguments = arguments;
        InitializeComponent();
        VersionText.Text = $"Версия {arguments.Version}";
        Loaded += UpdateWindow_Loaded;
    }

    private async void UpdateWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var progress = new Progress<UpdateProgress>(state =>
            {
                Progress.Value = state.Percentage;
                PercentageText.Text = $"{state.Percentage} %";
                StatusText.Text = state.Message;
            });
            await UpdateInstaller.ApplyAsync(_arguments, progress);

            Progress.Value = 100;
            PercentageText.Text = "100 %";
            StatusText.Text = "Обновление установлено. Приложение запускается снова...";
            WriteCompletionMarker(_arguments.Version);
            await Task.Delay(900);

            Process.Start(new ProcessStartInfo(_arguments.RestartExecutable)
            {
                WorkingDirectory = _arguments.TargetDirectory,
                UseShellExecute = true
            });
            Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            var logPath = UpdateInstaller.WriteErrorLog(exception);
            Progress.Value = 0;
            PercentageText.Text = "Ошибка";
            StatusText.Text = $"Не удалось установить обновление. Предыдущая версия восстановлена.\nЖурнал: {logPath}";
            CloseButton.Visibility = Visibility.Visible;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private static void WriteCompletionMarker(string version)
    {
        var updatesDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DieCutCatalog",
            "Updates");
        Directory.CreateDirectory(updatesDirectory);
        File.WriteAllText(Path.Combine(updatesDirectory, "update-completed.txt"), version.Trim());
    }
}
