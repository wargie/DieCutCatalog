using System.Windows;

namespace DieCutCatalog.Updater;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!UpdateArguments.TryParse(e.Args, out var arguments, out var error))
        {
            MessageBox.Show(error, "DieCut Catalog Updater", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(2);
            return;
        }

        MainWindow = new UpdateWindow(arguments!);
        MainWindow.Show();
    }
}
