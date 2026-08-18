using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using DieCutCatalog.Application.Employees;
using DieCutCatalog.Domain.Catalog;
using DieCutCatalog.Domain.Employees;

namespace DieCutCatalog.Desktop.Views;

public partial class EmployeesView : UserControl
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");
    private readonly List<EmployeeListRow> _allEmployees = [];
    private readonly ObservableCollection<EmployeeListRow> _visibleEmployees = [];
    private readonly ObservableCollection<ActivityRow> _activities = [];
    private CatalogApiClient? _api;
    private int _selectionVersion;

    public EmployeesView()
    {
        InitializeComponent();
        EmployeesGrid.ItemsSource = _visibleEmployees;
        ActivityGrid.ItemsSource = _activities;
    }

    internal void Initialize(CatalogApiClient api)
    {
        _api = api;
        Invalidate();
    }

    internal async Task UnlockAsync(string password)
    {
        if (_api is null) return;
        StatusText.Text = string.Empty;
        var selectedId = (EmployeesGrid.SelectedItem as EmployeeListRow)?.Profile.Id;
        var reports = await _api.GetEmployeeDirectoryAsync(password);
        _allEmployees.Clear();
        _allEmployees.AddRange(reports.Select(x => new EmployeeListRow(x)));
        ApplyFilter(selectedId);
    }

    internal void Invalidate()
    {
        _allEmployees.Clear();
        _visibleEmployees.Clear();
        _activities.Clear();
        ClearDetails();
    }

    internal void Clear()
    {
        _api = null;
        Invalidate();
    }
    private void ApplyFilter(Guid? selectedId = null)
    {
        var search = SearchBox.Text.Trim();
        var filtered = string.IsNullOrWhiteSpace(search)
            ? _allEmployees
            : _allEmployees.Where(x => x.SearchText.Contains(search, StringComparison.CurrentCultureIgnoreCase)).ToList();
        _visibleEmployees.Clear();
        foreach (var employee in filtered) _visibleEmployees.Add(employee);
        EmployeesGrid.SelectedItem = selectedId is null
            ? _visibleEmployees.FirstOrDefault()
            : _visibleEmployees.FirstOrDefault(x => x.Profile.Id == selectedId) ?? _visibleEmployees.FirstOrDefault();
        if (_visibleEmployees.Count == 0) ClearDetails();
    }

    private async Task LoadEmployeeAsync(EmployeeListRow selected)
    {
        if (_api is null) return;
        var version = ++_selectionVersion;
        StatusText.Text = string.Empty;
        try
        {
            var report = selected.Report;
            var photoBytes = await _api.DownloadPhotoAsync(report.Employee.PhotoUrl);
            if (version != _selectionVersion) return;
            ShowReport(report, photoBytes);
        }
        catch (CatalogApiException exception)
        {
            if (version == _selectionVersion) StatusText.Text = exception.Message;
        }
    }

    private void ShowReport(EmployeeActivityReport report, byte[]? photoBytes)
    {
        var employee = report.Employee;
        EmployeeNameText.Text = $"{employee.FirstName} {employee.LastName}".Trim();
        EmployeePositionText.Text = employee.Position ?? "Должность не указана";
        EmployeeEmailText.Text = employee.Email;
        EmployeePhoneText.Text = string.IsNullOrWhiteSpace(employee.Phone) ? "Телефон не указан" : employee.Phone;
        EmployeeContactsText.Text = string.IsNullOrWhiteSpace(employee.AdditionalContacts)
            ? "Дополнительные контакты не указаны"
            : employee.AdditionalContacts;
        EmployeeRoleText.Text = employee.Role == EmployeeRole.Administrator ? "Администратор" : "Оператор";
        DeleteEmployeeButton.IsEnabled = employee.IsActive;
        EmployeeStateText.Text = employee.IsActive
            ? employee.MustChangePassword ? "Требуется смена временного пароля" : "Учётная запись активна"
            : "Учётная запись отключена";

        KnivesCountText.Text = report.KnivesCount.ToString("N0", Russian);
        CreatedCountText.Text = report.CreatedCount.ToString("N0", Russian);
        DeletedCountText.Text = report.DeletedCount.ToString("N0", Russian);
        CirculationText.Text = report.TotalCirculation.ToString("N0", Russian);
        ActivityCountText.Text = $"Записей: {report.Activities.Count + report.AccessActivities.Count:N0}";

        EmployeePhoto.Source = ToImage(photoBytes);
        _activities.Clear();
        var rows = report.Activities.Select(x => new ActivityRow(x))
            .Concat(report.AccessActivities.Select(x => new ActivityRow(x)))
            .OrderByDescending(x => x.SortAt);
        foreach (var row in rows) _activities.Add(row);
    }

    private void ClearDetails()
    {
        ++_selectionVersion;
        EmployeePhoto.Source = null;
        EmployeeNameText.Text = "Выберите сотрудника";
        EmployeePositionText.Text = string.Empty;
        EmployeeEmailText.Text = string.Empty;
        EmployeePhoneText.Text = string.Empty;
        EmployeeContactsText.Text = string.Empty;
        EmployeeRoleText.Text = string.Empty;
        EmployeeStateText.Text = string.Empty;
        DeleteEmployeeButton.IsEnabled = false;
        KnivesCountText.Text = "0";
        CreatedCountText.Text = "0";
        DeletedCountText.Text = "0";
        CirculationText.Text = "0";
        ActivityCountText.Text = string.Empty;
        _activities.Clear();
    }

    private static BitmapImage? ToImage(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return null;
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private async void DeleteEmployee_Click(object sender, RoutedEventArgs e)
    {
        if (_api is null || EmployeesGrid.SelectedItem is not EmployeeListRow selected) return;
        var dialog = new PasswordConfirmationWindow(
            "Удалить сотрудника",
            $"Учётная запись «{selected.Name}» будет отключена, активные сеансы завершены. Контакты и история действий сохранятся в журнале.",
            "Удалить сотрудника")
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true) return;

        DeleteEmployeeButton.IsEnabled = false;
        try
        {
            await _api.DeleteEmployeeAsync(selected.Profile.Id, dialog.Password);
            await UnlockAsync(dialog.Password);
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("SuccessTextBrush");
            StatusText.Text = "Учётная запись сотрудника отключена.";
        }
        catch (CatalogApiException exception)
        {
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("ErrorTextBrush");
            StatusText.Text = exception.Message;
            DeleteEmployeeButton.IsEnabled = selected.Profile.IsActive;
        }
    }
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private async void EmployeesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EmployeesGrid.SelectedItem is EmployeeListRow selected)
            await LoadEmployeeAsync(selected);
    }

    private sealed class EmployeeListRow(EmployeeActivityReport report)
    {
        public EmployeeActivityReport Report { get; } = report;
        public EmployeeProfile Profile { get; } = report.Employee;
        public string Name { get; } = $"{report.Employee.LastName} {report.Employee.FirstName}".Trim();
        public string Position { get; } = report.Employee.Position ?? "";
        public string SearchText { get; } = string.Join(" ", new[]
        {
            report.Employee.FirstName, report.Employee.LastName, report.Employee.Email, report.Employee.Position,
            report.Employee.Phone, report.Employee.AdditionalContacts
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private sealed class ActivityRow
    {
        public ActivityRow(EmployeeActivityEntry entry)
        {
            SortAt = entry.OccurredAt;
            OccurredAt = entry.OccurredAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
            Action = EventName(entry.Type);
            DieCutNumber = entry.DieCutNumber;
            Equipment = entry.Equipment;
            Quantity = entry.Quantity?.ToString("N0", Russian) ?? "";
            Mileage = entry.MileageAfter.ToString("N0", Russian);
            Revolutions = entry.RevolutionsAfter.ToString("N0", Russian);
        }

        public ActivityRow(EmployeeAccessActivityEntry entry)
        {
            SortAt = entry.OccurredAt;
            OccurredAt = entry.OccurredAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
            Action = entry.Type == EmployeeAccessEventType.LoggedIn
                ? "Вошёл в систему"
                : "Вышел из системы";
            DieCutNumber = "";
            Equipment = "";
            Quantity = "";
            Mileage = "";
            Revolutions = "";
        }

        public DateTimeOffset SortAt { get; }
        public string OccurredAt { get; }
        public string Action { get; }
        public string DieCutNumber { get; }
        public string Equipment { get; }
        public string Quantity { get; }
        public string Mileage { get; }
        public string Revolutions { get; }

        private static string EventName(DieCutEventType type) => type switch
        {
            DieCutEventType.Created => "Создал нож",
            DieCutEventType.Updated => "Изменил параметры",
            DieCutEventType.CirculationAdded => "Добавил тираж",
            DieCutEventType.MileageReset => "Сбросил тираж",
            DieCutEventType.ReplacementInstalled => "Установил новый нож",
            DieCutEventType.Retired => "Списал нож",
            DieCutEventType.DrawingGenerated => "Сформировал PDF",
            DieCutEventType.Deleted => "Удалил нож",
            _ => type.ToString()
        };
    }
}
