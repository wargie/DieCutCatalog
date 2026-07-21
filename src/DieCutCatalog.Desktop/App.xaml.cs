using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace DieCutCatalog.Desktop;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += HandleDispatcherException;

        try
        {
            MainWindow = new MainWindow();
            MainWindow.Show();
        }
        catch (Exception exception)
        {
            ReportStartupError("Приложение не удалось запустить.", exception);
            Shutdown(-1);
        }
    }

    private static void HandleDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ReportStartupError("Ошибка интерфейса.", e.Exception);
        e.Handled = true;
    }

    private static void ReportStartupError(string message, Exception exception)
    {
        var logPath = Path.Combine(Path.GetTempPath(), "DieCutCatalog-startup.log");
        File.WriteAllText(logPath, exception.ToString());
        MessageBox.Show($"{message} Подробности сохранены в {logPath}",
            "DieCut Catalog", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
