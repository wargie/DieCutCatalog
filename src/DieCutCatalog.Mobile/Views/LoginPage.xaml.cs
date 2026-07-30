namespace DieCutCatalog.Mobile.Views;

public partial class LoginPage : ContentPage
{
    public LoginPage() => InitializeComponent();

    private async void Login_Clicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//main/catalog");
    }
}