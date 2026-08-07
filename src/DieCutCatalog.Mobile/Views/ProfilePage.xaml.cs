using DieCutCatalog.Mobile.Services;

namespace DieCutCatalog.Mobile.Views;

public partial class ProfilePage : ContentPage
{
    public ProfilePage()
    {
        InitializeComponent();
        LoadProfile();
        AppVersionLabel.Text = $"Версия {AppInfo.Current.VersionString}";
    }

    private void LoadProfile()
    {
        ProfileInitials.Text = SessionProfile.Initials;
        FirstNameEntry.Text = SessionProfile.FirstName;
        LastNameEntry.Text = SessionProfile.LastName;
        PositionEntry.Text = SessionProfile.Position;
        EmailEntry.Text = SessionProfile.Email;
        PhoneEntry.Text = SessionProfile.Phone;
        AdditionalContactsEditor.Text = SessionProfile.AdditionalContacts;
    }

    private async void ChoosePhoto_Clicked(object sender, EventArgs e)
    {
        try
        {
            var photo = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Выберите фото профиля",
                FileTypes = FilePickerFileType.Images
            });
            if (photo is null) return;
            await CatalogApiClient.Current.UploadPhotoAsync(photo);
            await DisplayAlert("Фото профиля", "Фотография сохранена на сервере.", "OK");
        }
        catch (Exception exception) when (exception is ApiException or PermissionException)
        {
            await DisplayAlert("Ошибка", exception.Message, "OK");
        }
    }

    private void Save_Clicked(object sender, EventArgs e)
    {
        ConfirmationPasswordEntry.Text = string.Empty;
        ConfirmationError.IsVisible = false;
        DangerOverlay.IsVisible = true;
        ConfirmationPasswordEntry.Focus();
    }

    private void CancelConfirmation_Clicked(object sender, EventArgs e)
    {
        DangerOverlay.IsVisible = false;
        ConfirmationPasswordEntry.Text = string.Empty;
        ConfirmationError.IsVisible = false;
    }

    private async void ConfirmSave_Clicked(object sender, EventArgs e)
    {
        ConfirmationError.IsVisible = false;
        ConfirmSaveButton.IsEnabled = false;
        try
        {
            await CatalogApiClient.Current.SaveProfileAsync(
                ConfirmationPasswordEntry.Text ?? string.Empty,
                FirstNameEntry.Text?.Trim() ?? string.Empty,
                LastNameEntry.Text?.Trim() ?? string.Empty,
                PositionEntry.Text?.Trim(),
                EmailEntry.Text?.Trim() ?? string.Empty,
                PhoneEntry.Text?.Trim(),
                AdditionalContactsEditor.Text?.Trim());

            DangerOverlay.IsVisible = false;
            ProfileInitials.Text = SessionProfile.Initials;
            await DisplayAlert("Профиль", "Изменения сохранены в базе данных.", "OK");
        }
        catch (ApiException exception)
        {
            ConfirmationError.Text = exception.Message;
            ConfirmationError.IsVisible = true;
        }
        finally
        {
            ConfirmSaveButton.IsEnabled = true;
        }
    }

    private async void CheckUpdates_Clicked(object sender, EventArgs e) =>
        await AndroidUpdateCoordinator.CheckAsync(this, notifyWhenCurrent: true);
}
