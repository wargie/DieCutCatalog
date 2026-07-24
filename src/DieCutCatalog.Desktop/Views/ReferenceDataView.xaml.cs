using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DieCutCatalog.Application.Catalog;
using DieCutCatalog.Domain.Catalog;
using Microsoft.Win32;

namespace DieCutCatalog.Desktop.Views;

public partial class ReferenceDataView : UserControl
{
    private readonly ObservableCollection<CatalogReferenceItem> _materials = [];
    private readonly ObservableCollection<CatalogReferenceItem> _figures = [];
    private readonly ObservableCollection<CatalogReferenceItem> _equipment = [];
    private readonly ObservableCollection<AuditLogRow> _audit = [];
    private CatalogApiClient? _api;
    private bool _isAdministrator;

    public event EventHandler? ReferencesChanged;
    public event EventHandler? BackRequested;

    public ReferenceDataView()
    {
        InitializeComponent();
        MaterialsGrid.ItemsSource = _materials;
        FiguresGrid.ItemsSource = _figures;
        EquipmentGrid.ItemsSource = _equipment;
        AuditGrid.ItemsSource = _audit;
    }

    internal async Task InitializeAsync(CatalogApiClient api, bool isAdministrator)
    {
        _api = api;
        _isAdministrator = isAdministrator;
        SetEditingEnabled(isAdministrator);
        await ReloadAsync();
    }

    internal async Task ReloadAsync()
    {
        if (_api is null) return;
        StatusText.Text = string.Empty;
        try
        {
            var references = await _api.GetCatalogReferencesAsync();
            Replace(_materials, references.Materials);
            Replace(_figures, references.Figures);
            Replace(_equipment, references.Equipment);
            await LoadAuditAsync();
        }
        catch (CatalogApiException exception) { SetStatus(exception.Message, true); }
    }

    internal void Clear()
    {
        _api = null;
        _materials.Clear();
        _figures.Clear();
        _equipment.Clear();
        _audit.Clear();
    }

    private async Task LoadAuditAsync()
    {
        if (_api is null) return;
        var result = await _api.SearchAuditLogAsync(AuditSearchBox.Text, 1, 500);
        _audit.Clear();
        foreach (var item in result.Items) _audit.Add(new AuditLogRow(item));
        AuditSummary.Text = result.Total > result.Items.Count
            ? $"Показано {result.Items.Count} из {result.Total}. Экспорт содержит весь журнал."
            : $"Записей: {result.Total}";
    }

    private async Task AddAsync(CatalogReferenceType type, TextBox box)
    {
        if (_api is null || !_isAdministrator) return;
        await RunAsync(async () =>
        {
            await _api.AddCatalogReferenceAsync(type, box.Text);
            box.Clear();
            await ReloadReferencesAsync();
        });
    }

    private async Task RenameAsync(CatalogReferenceType type, DataGrid grid, TextBox box)
    {
        if (_api is null || !_isAdministrator || grid.SelectedItem is not CatalogReferenceItem selected)
        {
            SetStatus("Выберите строку, которую нужно изменить.", true);
            return;
        }
        await RunAsync(async () =>
        {
            await _api.RenameCatalogReferenceAsync(type, selected.Id, box.Text);
            await ReloadReferencesAsync();
        });
    }

    private async Task DeleteAsync(CatalogReferenceType type, DataGrid grid)
    {
        if (_api is null || !_isAdministrator || grid.SelectedItem is not CatalogReferenceItem selected)
        {
            SetStatus("Выберите строку, которую нужно удалить.", true);
            return;
        }

        var dialog = new PasswordConfirmationWindow(
            "Удалить значение справочника",
            $"Значение «{selected.Name}» будет удалено. Если оно используется в карточках ножей, операция будет отклонена.",
            "Удалить")
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true) return;

        await RunAsync(async () =>
        {
            await _api.DeleteCatalogReferenceAsync(type, selected.Id, dialog.Password);
            await ReloadReferencesAsync();
            SetStatus("Значение удалено из справочника.", false);
        });
    }
    private async Task ReloadReferencesAsync()
    {
        var references = await _api!.GetCatalogReferencesAsync();
        Replace(_materials, references.Materials);
        Replace(_figures, references.Figures);
        Replace(_equipment, references.Equipment);
        SetStatus("Справочник сохранён.", false);
        ReferencesChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task RunAsync(Func<Task> action)
    {
        try { await action(); }
        catch (CatalogApiException exception) { SetStatus(exception.Message, true); }
        catch (Exception exception) { SetStatus($"Операция не выполнена: {exception.Message}", true); }
    }

    private async Task ExportAsync(string format)
    {
        if (_api is null) return;
        var dialog = new SaveFileDialog
        {
            Title = "Экспорт журнала действий",
            Filter = format == "pdf" ? "PDF (*.pdf)|*.pdf" : "CSV (*.csv)|*.csv",
            FileName = $"knife-audit-{DateTime.Now:yyyyMMdd-HHmmss}.{format}"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        await RunAsync(async () =>
        {
            var bytes = await _api.ExportAuditLogAsync(AuditSearchBox.Text, format);
            await File.WriteAllBytesAsync(dialog.FileName, bytes);
            SetStatus($"Журнал сохранён: {dialog.FileName}", false);
        });
    }

    private void SetEditingEnabled(bool enabled)
    {
        foreach (var control in new Control[]
        {
            MaterialNameBox, FigureNameBox, EquipmentNameBox,
            AddMaterialButton, RenameMaterialButton, AddFigureButton,
            RenameFigureButton, DeleteMaterialButton, DeleteFigureButton, AddEquipmentButton, RenameEquipmentButton, DeleteEquipmentButton
        }) control.IsEnabled = enabled;
        if (!enabled)
            SetStatus("Справочники доступны для просмотра. Изменения может вносить администратор.", false);
    }

    private void SetStatus(string message, bool error)
    {
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource(
            error ? "ErrorTextBrush" : "SuccessTextBrush");
        StatusText.Text = message;
    }

    private static void Replace(
        ObservableCollection<CatalogReferenceItem> target,
        IEnumerable<CatalogReferenceItem> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }

    private static void SelectName(DataGrid grid, TextBox box)
    {
        if (grid.SelectedItem is CatalogReferenceItem item) box.Text = item.Name;
    }

    private void MaterialsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => SelectName(MaterialsGrid, MaterialNameBox);
    private void FiguresGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => SelectName(FiguresGrid, FigureNameBox);
    private void EquipmentGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => SelectName(EquipmentGrid, EquipmentNameBox);
    private void BackToCatalog_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
    private async void DeleteMaterial_Click(object sender, RoutedEventArgs e) => await DeleteAsync(CatalogReferenceType.Material, MaterialsGrid);
    private async void DeleteFigure_Click(object sender, RoutedEventArgs e) => await DeleteAsync(CatalogReferenceType.Figure, FiguresGrid);
    private async void DeleteEquipment_Click(object sender, RoutedEventArgs e) => await DeleteAsync(CatalogReferenceType.Equipment, EquipmentGrid);    private async void AddMaterial_Click(object sender, RoutedEventArgs e) => await AddAsync(CatalogReferenceType.Material, MaterialNameBox);
    private async void RenameMaterial_Click(object sender, RoutedEventArgs e) => await RenameAsync(CatalogReferenceType.Material, MaterialsGrid, MaterialNameBox);
    private async void AddFigure_Click(object sender, RoutedEventArgs e) => await AddAsync(CatalogReferenceType.Figure, FigureNameBox);
    private async void RenameFigure_Click(object sender, RoutedEventArgs e) => await RenameAsync(CatalogReferenceType.Figure, FiguresGrid, FigureNameBox);
    private async void AddEquipment_Click(object sender, RoutedEventArgs e) => await AddAsync(CatalogReferenceType.Equipment, EquipmentNameBox);
    private async void RenameEquipment_Click(object sender, RoutedEventArgs e) => await RenameAsync(CatalogReferenceType.Equipment, EquipmentGrid, EquipmentNameBox);
    private async void SearchAudit_Click(object sender, RoutedEventArgs e) => await RunAsync(LoadAuditAsync);
    private async void ExportCsv_Click(object sender, RoutedEventArgs e) => await ExportAsync("csv");
    private async void ExportPdf_Click(object sender, RoutedEventArgs e) => await ExportAsync("pdf");
    private async void AuditSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await RunAsync(LoadAuditAsync);
    }

    private sealed class AuditLogRow
    {
        private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

        public AuditLogRow(AuditLogEntry entry)
        {
            OccurredAt = entry.OccurredAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
            DieCutNumber = entry.DieCutNumber;
            Equipment = entry.Equipment;
            Action = EventName(entry.Type);
            Quantity = entry.Quantity?.ToString("N0", Russian) ?? "";
            Mileage = $"{entry.MileageBefore:N0} -> {entry.MileageAfter:N0}";
            RunLength = $"{entry.RunLengthMetersBefore:N2} -> {entry.RunLengthMetersAfter:N2}";
            Revolutions = $"{entry.RevolutionsBefore:N0} -> {entry.RevolutionsAfter:N0}";
            EmployeeName = entry.EmployeeName;
        }

        public string OccurredAt { get; }
        public string DieCutNumber { get; }
        public string Equipment { get; }
        public string Action { get; }
        public string Quantity { get; }
        public string Mileage { get; }
        public string RunLength { get; }
        public string Revolutions { get; }
        public string EmployeeName { get; }

        private static string EventName(DieCutEventType type) => type switch
        {
            DieCutEventType.Created => "Нож создан",
            DieCutEventType.Updated => "Параметры изменены",
            DieCutEventType.CirculationAdded => "Добавлен тираж",
            DieCutEventType.MileageReset => "Пробег сброшен",
            DieCutEventType.ReplacementInstalled => "Установлен новый нож",
            DieCutEventType.Retired => "Нож списан",
            DieCutEventType.DrawingGenerated => "PDF сформирован",
            DieCutEventType.Deleted => "Нож удалён",
            _ => type.ToString()
        };
    }
}
