using DieCutCatalog.Mobile.Models;
using DieCutCatalog.Mobile.Views;

namespace DieCutCatalog.Mobile.Services;

public static class AndroidUpdateCoordinator
{
    private static bool automaticCheckCompleted;

    public static AndroidUpdateManifest? PendingUpdate { get; private set; }

    public static async Task CheckAsync(Page owner, bool notifyWhenCurrent)
    {
        if (!notifyWhenCurrent && automaticCheckCompleted) return;
        if (!notifyWhenCurrent) automaticCheckCompleted = true;

        try
        {
            var manifest = await CatalogApiClient.Current.GetLatestAndroidUpdateAsync();
            var currentCode = int.TryParse(AppInfo.Current.BuildString, out var parsed) ? parsed : 0;
            if (manifest is null || manifest.VersionCode <= currentCode)
            {
                if (notifyWhenCurrent)
                    await owner.DisplayAlert("Обновления", $"Установлена актуальная версия {AppInfo.Current.VersionString}.", "OK");
                return;
            }

            PendingUpdate = manifest;
            await Shell.Current.GoToAsync(nameof(AndroidUpdatePage));
        }
        catch (ApiException exception)
        {
            if (notifyWhenCurrent)
                await owner.DisplayAlert("Проверка обновлений", exception.Message, "OK");
        }
    }
}
