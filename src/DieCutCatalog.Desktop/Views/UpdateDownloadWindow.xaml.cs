using System.ComponentModel;
using System.Windows;
using DieCutCatalog.Application.Updates;

namespace DieCutCatalog.Desktop.Views;

public partial class UpdateDownloadWindow : Window
{
    private readonly CatalogApiClient _api;
    private readonly ClientUpdateManifest _manifest;
    private readonly string _destinationPath;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _isRunning = true;

    internal UpdateDownloadWindow(CatalogApiClient api, ClientUpdateManifest manifest, string destinationPath)
    {
        _api = api;
        _manifest = manifest;
        _destinationPath = destinationPath;
        InitializeComponent();
        VersionText.Text = $"{manifest.ReleaseName} · версия {manifest.Version}";
        Loaded += UpdateDownloadWindow_Loaded;
        Closing += UpdateDownloadWindow_Closing;
    }

    private async void UpdateDownloadWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var progress = new Progress<ClientUpdateDownloadProgress>(UpdateProgress);
            await _api.DownloadUpdateAsync(_manifest, _destinationPath, progress, _cancellation.Token);
            DownloadProgress.Value = 100;
            PercentageText.Text = "100 %";
            SizeText.Text = $"Загружено {FormatSize(_manifest.Size)}";
            StatusText.Text = "Пакет загружен, проверен и готов к установке.";
            _isRunning = false;
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            _isRunning = false;
            DialogResult = false;
        }
        catch (Exception exception)
        {
            _isRunning = false;
            StatusText.Text = exception.Message;
            StatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            CancelButton.Content = "Закрыть";
        }
    }

    private void UpdateProgress(ClientUpdateDownloadProgress progress)
    {
        DownloadProgress.Value = progress.Percentage;
        PercentageText.Text = $"{progress.Percentage} %";
        SizeText.Text = $"Загружено {FormatSize(progress.BytesReceived)} из {FormatSize(progress.TotalBytes)}";
        if (progress.Percentage >= 100) StatusText.Text = "Проверка целостности пакета...";
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            CancelButton.IsEnabled = false;
            StatusText.Text = "Отмена загрузки...";
            _cancellation.Cancel();
            return;
        }

        DialogResult = false;
    }

    private void UpdateDownloadWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isRunning) return;
        e.Cancel = true;
        CancelButton_Click(CancelButton, new RoutedEventArgs());
    }

    private static string FormatSize(long bytes) => $"{bytes / 1024d / 1024d:N1} МБ";
}
