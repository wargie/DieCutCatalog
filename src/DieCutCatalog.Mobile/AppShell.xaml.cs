namespace DieCutCatalog.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(Views.KnifeDetailPage), typeof(Views.KnifeDetailPage));
        Routing.RegisterRoute(nameof(Views.AddCirculationPage), typeof(Views.AddCirculationPage));
        Routing.RegisterRoute(nameof(Views.ProfilePage), typeof(Views.ProfilePage));
    }
}
