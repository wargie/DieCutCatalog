using DieCutCatalog.Mobile.Services;

namespace DieCutCatalog.Mobile.Views;

public partial class LoginPage : ContentPage
{
    public LoginPage() => InitializeComponent();

    private async void Login_Clicked(object sender, EventArgs e)
    {
        LoginError.IsVisible = false;
        LoginButton.IsEnabled = false;
        LoginProgress.IsRunning = true;
        try
        {
            await CatalogApiClient.Current.LoginAsync(
                EmailEntry.Text?.Trim() ?? string.Empty,
                PasswordEntry.Text ?? string.Empty);
            PasswordEntry.Text = string.Empty;
            await Shell.Current.GoToAsync("//main");
        }
        catch (ApiException exception)
        {
            LoginError.Text = exception.Message;
            LoginError.IsVisible = true;
        }
        finally
        {
            LoginProgress.IsRunning = false;
            LoginButton.IsEnabled = true;
        }
    }
}
