using Android.Content;
using Android.OS;
using Android.Provider;
using AndroidX.Core.Content;

namespace DieCutCatalog.Mobile.Services;

public static class AndroidPackageInstaller
{
    public static bool HasInstallPermission()
    {
        var context = Android.App.Application.Context;
        return Build.VERSION.SdkInt < BuildVersionCodes.O
            || context.PackageManager?.CanRequestPackageInstalls() == true;
    }

    public static void OpenInstallPermissionSettings()
    {
        var context = Android.App.Application.Context;
        var intent = new Intent(
            Settings.ActionManageUnknownAppSources,
            Android.Net.Uri.Parse($"package:{context.PackageName}"));
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }

    public static void Install(string apkPath)
    {
        var context = Android.App.Application.Context;
        var file = new Java.IO.File(apkPath);
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(context, $"{context.PackageName}.fileProvider", file);
        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(uri, "application/vnd.android.package-archive");
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);
        context.StartActivity(intent);
    }
}
