using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DieCutCatalog.Application.Employees;
using DieCutCatalog.Application.Updates;
using DieCutCatalog.Domain.Employees;
using DieCutCatalog.Desktop.Views;
using Microsoft.Win32;

namespace DieCutCatalog.Desktop;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly CatalogApiClient _api = new();
    private EmployeeProfile? _profile;
    private bool _isClosing;

    public MainWindow()
    {
        InitializeComponent();
        ApplicationVersionText.Text = $"Версия {GetCurrentVersion()}";
        ReferenceDataView.ReferencesChanged += async (_, _) => await CatalogView.ReloadReferenceDataAsync();
        ReferenceDataView.BackRequested += (_, _) => ShowCatalog();

        Closing += MainWindow_Closing;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isClosing) return;

        e.Cancel = true;
        _isClosing = true;
        _ = LogoutAndCloseAsync();
    }

    private async Task LogoutAndCloseAsync()
    {
        try { await _api.LogoutAsync(); }
        catch { }
        finally
        {
            _api.Dispose();
            _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(Close));
        }
    }
    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(LoginButton, async () =>
        {
            HideError(LoginError);
            _api.Configure(ServerAddressBox.Text);
            var result = await _api.LoginAsync(EmailBox.Text, PasswordBox.Password);
            _profile = result.Profile;
            if (result.MustChangePassword)
            {
                TemporaryPasswordBox.Password = PasswordBox.Password;
                PasswordChangeOverlay.Visibility = Visibility.Visible;
                RequiredNewPasswordBox.Focus();
                return;
            }
            await OpenShellAsync();

        }, LoginError);
    }

    private void LoginPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Login_Click(LoginButton, new RoutedEventArgs());
    }

    private async void CompletePasswordChange_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync((Button)sender, async () =>
        {
            HideError(RequiredPasswordError);
            if (RequiredNewPasswordBox.Password != RequiredPasswordConfirmationBox.Password)
                throw new CatalogApiException("Новые пароли не совпадают.");
            await _api.ChangePasswordAsync(TemporaryPasswordBox.Password, RequiredNewPasswordBox.Password);
            _profile = _profile! with { MustChangePassword = false };
            TemporaryPasswordBox.Clear();
            RequiredNewPasswordBox.Clear();
            RequiredPasswordConfirmationBox.Clear();
            PasswordChangeOverlay.Visibility = Visibility.Collapsed;
            await OpenShellAsync();
        }, RequiredPasswordError);
    }

    private async Task OpenShellAsync()
    {
        PasswordBox.Clear();
        PopulateProfile();
        await LoadPhotoAsync();
        await CatalogView.InitializeAsync(_api, _profile?.Role == EmployeeRole.Administrator);
        await ReferenceDataView.InitializeAsync(_api, _profile?.Role == EmployeeRole.Administrator);
        EmployeesView.Initialize(_api);
        await CatalogView.ReloadReferenceDataAsync();
        LoginView.Visibility = Visibility.Collapsed;
        ShellView.Visibility = Visibility.Visible;
        ShowCatalog();
        _ = CheckForUpdatesAsync(notifyWhenCurrent: false);
    }

    private void PopulateProfile()
    {
        if (_profile is null) return;
        SidebarUserName.Text = $"{_profile.FirstName} {_profile.LastName}";
        SidebarUserEmail.Text = _profile.Email;
        FirstNameBox.Text = _profile.FirstName;
        LastNameBox.Text = _profile.LastName;
        PositionBox.Text = _profile.Position;
        PhoneBox.Text = _profile.Phone;
        ContactsBox.Text = _profile.AdditionalContacts;
        ProfileEmailBox.Text = _profile.Email;
        NewEmailBox.Text = _profile.Email;
        CreateEmployeePanel.Visibility = _profile.Role == EmployeeRole.Administrator ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task LoadPhotoAsync()
    {
        try
        {
            var bytes = await _api.DownloadPhotoAsync(_profile?.PhotoUrl);
            if (bytes is null) { ProfilePhoto.Source = null; return; }
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
            ProfilePhoto.Source = image;
        }
        catch (CatalogApiException) { ProfilePhoto.Source = null; }
    }

    private void ShowCatalog_Click(object sender, RoutedEventArgs e) => ShowCatalog();
    private void ShowCatalog()
    {
        CatalogView.Visibility = Visibility.Visible;
        ReferenceDataView.Visibility = Visibility.Collapsed;
        EmployeesView.Visibility = Visibility.Collapsed;
        EmployeeView.Visibility = Visibility.Collapsed;
        CatalogNavButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
        ReferenceDataNavButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent;
        EmployeesNavButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent;
        EmployeeNavButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent;
    }
    private async void ShowReferenceData_Click(object sender, RoutedEventArgs e)
    {
        CatalogView.Visibility = Visibility.Collapsed;
        ReferenceDataView.Visibility = Visibility.Visible;
        EmployeesView.Visibility = Visibility.Collapsed;
        EmployeeView.Visibility = Visibility.Collapsed;
        CatalogNavButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent;
        ReferenceDataNavButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
        EmployeesNavButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent;
        EmployeeNavButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent;
        await ReferenceDataView.ReloadAsync();
    }
    private async void ShowEmployees_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PasswordConfirmationWindow(
            "Доступ к справочнику сотрудников",
            "Справочник содержит персональные данные и историю действий. Для доступа введите мастер-пароль администратора.",
            "Открыть справочник")
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true) return;

        await RunBusyAsync((Button)sender, async () =>
        {
            await EmployeesView.UnlockAsync(dialog.Password);
            CatalogView.Visibility = Visibility.Collapsed;
            ReferenceDataView.Visibility = Visibility.Collapsed;
            EmployeesView.Visibility = Visibility.Visible;
            EmployeeView.Visibility = Visibility.Collapsed;
            CatalogNavButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent;
            ReferenceDataNavButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent;
            EmployeesNavButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
            EmployeeNavButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent;
        });
    }    private void ShowEmployee_Click(object sender, RoutedEventArgs e)
    {
        CatalogView.Visibility = Visibility.Collapsed;
        ReferenceDataView.Visibility = Visibility.Collapsed;
        EmployeesView.Visibility = Visibility.Collapsed;
        EmployeeView.Visibility = Visibility.Visible;
        CatalogNavButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent;
        ReferenceDataNavButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent;
        EmployeesNavButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent;
        EmployeeNavButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
    }

    private async void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync((Button)sender, async () =>
        {
            ProfileStatus.Text = string.Empty;
            _profile = await _api.UpdateProfileAsync(FirstNameBox.Text, LastNameBox.Text, NullIfEmpty(PositionBox.Text), NullIfEmpty(PhoneBox.Text), NullIfEmpty(ContactsBox.Text));
            PopulateProfile();
            ProfileStatus.Text = "Сохранено";
        });
    }

    private async void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync((Button)sender, async () =>
        {
            await _api.ChangePasswordAsync(CurrentPasswordBox.Password, NewPasswordBox.Password);
            CurrentPasswordBox.Clear(); NewPasswordBox.Clear();
            MessageBox.Show("Пароль изменён.", "DieCut Catalog", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private async void ChangeEmail_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync((Button)sender, async () =>
        {
            await _api.ChangeEmailAsync(EmailPasswordBox.Password, NewEmailBox.Text);
            _profile = _profile! with { Email = NewEmailBox.Text.Trim() };
            EmailPasswordBox.Clear(); PopulateProfile();
            MessageBox.Show("Адрес электронной почты изменён.", "DieCut Catalog", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private async void UploadPhoto_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Выберите фотографию", Filter = "Изображения|*.jpg;*.jpeg;*.png;*.webp", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        await RunBusyAsync((Button)sender, async () =>
        {
            _profile = await _api.UploadPhotoAsync(dialog.FileName);
            PopulateProfile(); await LoadPhotoAsync();
        });
    }

    private async void CreateEmployee_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync((Button)sender, async () =>
        {
            var result = await _api.CreateEmployeeAsync(EmployeeEmailBox.Text, EmployeeFirstNameBox.Text, EmployeeLastNameBox.Text,
                NullIfEmpty(EmployeePositionBox.Text), NullIfEmpty(EmployeePhoneBox.Text), EmployeeAdministratorBox.IsChecked == true);
            EmployeeEmailBox.Clear(); EmployeeFirstNameBox.Clear(); EmployeeLastNameBox.Clear(); EmployeePositionBox.Clear(); EmployeePhoneBox.Clear();
            EmployeeAdministratorBox.IsChecked = false;
            EmployeesView.Invalidate();
            if (result.EmailDelivered)
            {
                MessageBox.Show($"Учётная запись для {result.Profile.Email} создана. Временный пароль отправлен по почте.",
                    "DieCut Catalog", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!string.IsNullOrWhiteSpace(result.TemporaryPassword))
            {
                try { Clipboard.SetText(result.TemporaryPassword); }
                catch { }
            }
            MessageBox.Show(
                $"Учётная запись для {result.Profile.Email} создана, но почтовый сервер не настроен.\n\n" +
                $"Временный пароль: {result.TemporaryPassword}\n\nПароль скопирован в буфер обмена. Передайте его сотруднику безопасным способом.",
                "Временный пароль", MessageBoxButton.OK, MessageBoxImage.Warning);        });
    }

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync((Button)sender, async () =>
        {
            await _api.LogoutAsync();
            _profile = null; ProfilePhoto.Source = null; CatalogView.Clear(); ReferenceDataView.Clear(); EmployeesView.Clear();
            ShellView.Visibility = Visibility.Collapsed; LoginView.Visibility = Visibility.Visible; EmailBox.Focus();
        });
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync((Button)sender, () => CheckForUpdatesAsync(notifyWhenCurrent: true));
    }

    private async Task CheckForUpdatesAsync(bool notifyWhenCurrent)
    {
        try
        {
            var manifest = await _api.GetLatestUpdateAsync();
            var currentVersion = GetCurrentVersion();
            if (manifest is null || !ClientUpdateVersion.IsNewer(manifest.Version, currentVersion))
            {
                if (notifyWhenCurrent)
                {
                    MessageBox.Show($"Установлена актуальная версия {currentVersion}.", "Обновления DieCut Catalog", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            var notes = string.IsNullOrWhiteSpace(manifest.Notes) ? string.Empty : $"\n\n{manifest.Notes.Trim()}";
            var shouldDownload = MessageBox.Show(
                $"Доступно обновление {manifest.ReleaseName} (версия {manifest.Version}).{notes}\n\nСкачать обновление?",
                "Доступно обновление",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (shouldDownload != MessageBoxResult.Yes) return;

            var dialog = new SaveFileDialog
            {
                Title = "Сохранить обновление DieCut Catalog",
                FileName = manifest.FileName,
                DefaultExt = ".zip",
                Filter = "Архив ZIP|*.zip",
                AddExtension = true,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != true) return;

            CheckUpdatesButton.IsEnabled = false;
            try { await _api.DownloadUpdateAsync(manifest, dialog.FileName); }
            finally { CheckUpdatesButton.IsEnabled = true; }

            MessageBox.Show(
                "Обновление загружено и проверено. Закройте приложение, распакуйте архив и замените файлы клиента.",
                "Обновление загружено",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{dialog.FileName}\"") { UseShellExecute = true });
        }
        catch (Exception exception) when (!notifyWhenCurrent)
        {
            Debug.WriteLine($"Automatic update check failed: {exception.Message}");
        }
    }

    private static string GetCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(MainWindow).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
    }
    private async Task RunBusyAsync(Button button, Func<Task> action, TextBlock? inlineError = null)
    {
        button.IsEnabled = false;
        try { await action(); }
        catch (CatalogApiException exception)
        {
            if (inlineError is not null) { inlineError.Text = exception.Message; inlineError.Visibility = Visibility.Visible; }
            else MessageBox.Show(exception.Message, "DieCut Catalog", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Операция не выполнена: {exception.Message}", "DieCut Catalog", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { button.IsEnabled = true; }
    }

    private static void HideError(TextBlock error) { error.Text = string.Empty; error.Visibility = Visibility.Collapsed; }
    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
