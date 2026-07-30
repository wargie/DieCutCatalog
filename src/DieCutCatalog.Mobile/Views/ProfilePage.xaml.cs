namespace DieCutCatalog.Mobile.Views;

public partial class ProfilePage : ContentPage
{
    public ProfilePage()
    {
        InitializeComponent();
        LoadProfile();
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

    private async void ChoosePhoto_Clicked(object sender, EventArgs e) =>
        await DisplayAlert("Фото профиля", "Загрузка фотографии будет подключена вместе с серверным профилем.", "OK");

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
        if (ConfirmationPasswordEntry.Text != SessionProfile.Password)
        {
            ConfirmationError.IsVisible = true;
            return;
        }

        SessionProfile.FirstName = FirstNameEntry.Text?.Trim() ?? string.Empty;
        SessionProfile.LastName = LastNameEntry.Text?.Trim() ?? string.Empty;
        SessionProfile.Position = PositionEntry.Text?.Trim() ?? string.Empty;
        SessionProfile.Email = EmailEntry.Text?.Trim() ?? string.Empty;
        SessionProfile.Phone = PhoneEntry.Text?.Trim() ?? string.Empty;
        SessionProfile.AdditionalContacts = AdditionalContactsEditor.Text?.Trim() ?? string.Empty;

        DangerOverlay.IsVisible = false;
        ProfileInitials.Text = SessionProfile.Initials;
        await DisplayAlert("Профиль", "Изменения сохранены.", "OK");
    }
}
