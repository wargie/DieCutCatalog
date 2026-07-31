using System.Diagnostics;
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
            var progress = new Progress<string>(message => StatusText.Text = message);
            await UpdateInstaller.ApplyAsync(_arguments, progress);

            Progress.IsIndeterminate = false;
            Progress.Value = 100;
            StatusText.Text = "Обновление установлено. Приложение запускается снова...";
            await Task.Delay(700);

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
            Progress.IsIndeterminate = false;
            Progress.Value = 0;
            StatusText.Text = $"Не удалось установить обновление. Предыдущая версия восстановлена.\nЖурнал: {logPath}";
            CloseButton.Visibility = Visibility.Visible;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
