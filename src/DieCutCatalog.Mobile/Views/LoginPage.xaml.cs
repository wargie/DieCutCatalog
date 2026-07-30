namespace DieCutCatalog.Mobile.Views;

public partial class LoginPage : ContentPage
{
    public LoginPage() => InitializeComponent();

    private async void Login_Clicked(object sender, EventArgs e)
    {
        SessionProfile.Email = EmailEntry.Text?.Trim() ?? string.Empty;
        SessionProfile.Password = PasswordEntry.Text ?? string.Empty;
        await Shell.Current.GoToAsync("//main");
    }
}
