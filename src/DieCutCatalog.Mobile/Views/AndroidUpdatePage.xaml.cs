using Android.Content;
using DieCutCatalog.Mobile.Models;
using DieCutCatalog.Mobile.Services;

namespace DieCutCatalog.Mobile.Views;

public partial class AndroidUpdatePage : ContentPage
{
    private AndroidUpdateManifest? manifest;
    private string? downloadedApk;
    private CancellationTokenSource? downloadCancellation;

    public AndroidUpdatePage() => InitializeComponent();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        manifest = AndroidUpdateCoordinator.PendingUpdate;
        if (manifest is null)
        {
            Shell.Current.GoToAsync("..");
            return;
        }

        ReleaseNameLabel.Text = manifest.ReleaseName;
        CurrentVersionLabel.Text = AppInfo.Current.VersionString;
        NewVersionLabel.Text = manifest.Version;
        PackageSizeLabel.Text = FormatBytes(manifest.Size);
        NotesLabel.Text = string.IsNullOrWhiteSpace(manifest.Notes)
            ? "Исправления и улучшения мобильного клиента."
            : manifest.Notes.Trim();
        LaterButton.IsVisible = !manifest.Required;
    }

    protected override bool OnBackButtonPressed() => manifest?.Required == true || base.OnBackButtonPressed();

    private async void Install_Clicked(object sender, EventArgs e)
    {
        if (manifest is null) return;
        UpdateErrorLabel.IsVisible = false;
        PermissionHintLabel.IsVisible = false;

        if (!AndroidPackageInstaller.HasInstallPermission())
        {
            PermissionHintLabel.IsVisible = true;
            AndroidPackageInstaller.OpenInstallPermissionSettings();
            return;
        }

        try
        {
            InstallButton.IsEnabled = false;
            LaterButton.IsEnabled = false;
            ProgressPanel.IsVisible = true;
            downloadCancellation = new CancellationTokenSource();
            var progress = new Progress<UpdateDownloadProgress>(value =>
            {
                DownloadProgress.Progress = value.Fraction;
                ProgressLabel.Text = $"{FormatBytes(value.Received)} из {FormatBytes(value.Total)}";
            });
            downloadedApk ??= await CatalogApiClient.Current.DownloadAndroidUpdateAsync(
                manifest,
                progress,
                downloadCancellation.Token);

            ProgressLabel.Text = "APK проверен. Открываю системный установщик…";
            AndroidPackageInstaller.Install(downloadedApk);
        }
        catch (Exception exception) when (exception is ApiException or IOException or ActivityNotFoundException)
        {
            UpdateErrorLabel.Text = exception.Message;
            UpdateErrorLabel.IsVisible = true;
        }
        finally
        {
            InstallButton.IsEnabled = true;
            LaterButton.IsEnabled = true;
            downloadCancellation?.Dispose();
            downloadCancellation = null;
        }
    }

    private async void Later_Clicked(object sender, EventArgs e) => await Shell.Current.GoToAsync("..");

    private static string FormatBytes(long bytes) =>
        bytes >= 1024 * 1024
            ? $"{bytes / 1024d / 1024d:N1} МБ"
            : $"{bytes / 1024d:N0} КБ";
}
