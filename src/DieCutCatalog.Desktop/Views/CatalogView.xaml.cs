using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DieCutCatalog.Application.Catalog;
using DieCutCatalog.Domain.Catalog;

namespace DieCutCatalog.Desktop.Views;

public partial class CatalogView : UserControl
{
    private const int PageSize = 100;
    private readonly ObservableCollection<DieCutRow> _rows = [];
    private readonly IReadOnlyList<StatusOption> _statuses =
    [
        new(DieCutStatus.Active, "В работе"),
        new(DieCutStatus.NeedsInspection, "Требует проверки"),
        new(DieCutStatus.Retired, "Списан")
    ];
    private CatalogApiClient? _api;
    private Guid? _editingId;
    private int _page = 1;
    private int _total;
    private bool _loadingCard;

    public CatalogView()
    {
        InitializeComponent();
        DieCutsGrid.ItemsSource = _rows;
        StatusBox.ItemsSource = _statuses;
        StatusBox.SelectedIndex = 0;
    }

    internal async Task InitializeAsync(CatalogApiClient api, bool canImport)
    {
        _api = api;
        ImportExcelButton.Visibility = canImport ? Visibility.Visible : Visibility.Collapsed;
        _page = 1;
        try
        {
            await LoadFacetsAsync();
        }
        catch (CatalogApiException exception)
        {
            CatalogError.Text = exception.Message;
        }
        await LoadPageAsync();
    }

    internal void Clear()
    {
        _api = null;
        _rows.Clear();
        CloseEditor();
        CatalogSummary.Text = "Нет подключения к серверу";
    }

    private async Task LoadFacetsAsync()
    {
        if (_api is null) return;
        var facets = await _api.GetCatalogFacetsAsync();
        SetFilterItems(EquipmentFilter, facets.Equipment);
        SetFilterItems(MaterialFilter, facets.Materials);
        SetFilterItems(ShapeFilter, facets.Shapes);
        SetEditorItems(EquipmentBox, facets.Equipment);
        SetEditorItems(MaterialBox, facets.Materials);
        SetEditorItems(ShapeBox, facets.Shapes);
    }

    private async Task LoadPageAsync()
    {
        if (_api is null) return;
        CatalogError.Text = string.Empty;
        SetNavigationEnabled(false);
        try
        {
            var result = await _api.SearchDieCutsAsync(
                SearchBox.Text,
                SelectedFilter(EquipmentFilter),
                SelectedFilter(MaterialFilter),
                SelectedFilter(ShapeFilter),
                _page,
                PageSize);
            _total = result.Total;
            _rows.Clear();
            foreach (var item in result.Items) _rows.Add(new DieCutRow(item, StatusName(item.Status)));

            var pageCount = Math.Max(1, (int)Math.Ceiling(_total / (double)PageSize));
            CatalogSummary.Text = _total == 0 ? "Ножи не найдены" : $"Найдено: {_total}";
            PageText.Text = $"Страница {_page} из {pageCount}";
            PreviousPageButton.IsEnabled = _page > 1;
            NextPageButton.IsEnabled = _page < pageCount;
        }
        catch (CatalogApiException exception)
        {
            CatalogError.Text = exception.Message;
            CatalogSummary.Text = "Не удалось загрузить каталог";
        }
    }

    private async void ApplyFilters_Click(object sender, RoutedEventArgs e)
    {
        _page = 1;
        await LoadPageAsync();
    }

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _page = 1;
        await LoadPageAsync();
    }

    private async void PreviousPage_Click(object sender, RoutedEventArgs e)
    {
        if (_page <= 1) return;
        _page--;
        await LoadPageAsync();
    }

    private async void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if (_page * PageSize >= _total) return;
        _page++;
        await LoadPageAsync();
    }

    private async void DieCutsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingCard || DieCutsGrid.SelectedItem is not DieCutRow row || _api is null) return;
        _loadingCard = true;
        try
        {
            CatalogError.Text = string.Empty;
            var details = await _api.GetDieCutAsync(row.Id);
            FillEditor(details);
            OpenEditor();
        }
        catch (CatalogApiException exception)
        {
            CatalogError.Text = exception.Message;
        }
        finally
        {
            _loadingCard = false;
        }
    }

    private async void ImportExcel_Click(object sender, RoutedEventArgs e)
    {
        if (_api is null) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите Excel-каталог",
            Filter = "Книга Excel (*.xlsx)|*.xlsx",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        ImportExcelButton.IsEnabled = false;
        CatalogError.Text = string.Empty;
        try
        {
            var preview = await _api.PreviewExcelImportAsync(dialog.FileName);
            var previewWindow = new ExcelImportPreviewWindow(dialog.FileName, preview)
            {
                Owner = Window.GetWindow(this)
            };
            if (previewWindow.ShowDialog() != true) return;
            var result = await _api.CommitExcelImportAsync(dialog.FileName, previewWindow.OverwriteExisting);
            MessageBox.Show(
                $"Добавлено: {result.ImportedRows}\nОбновлено: {result.UpdatedRows}\nПропущено: {result.SkippedRows}",
                "Импорт завершён", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadFacetsAsync();
            _page = 1;
            await LoadPageAsync();
        }
        catch (CatalogApiException exception)
        {
            CatalogError.Text = exception.Message;
        }
        finally
        {
            ImportExcelButton.IsEnabled = true;
        }
    }

    private void NewDieCut_Click(object sender, RoutedEventArgs e)
    {
        _editingId = null;
        DieCutsGrid.SelectedItem = null;
        ClearEditorFields();
        EditorTitle.Text = "Новый нож";
        EditorStatus.Text = string.Empty;
        OpenEditor();
        NumberBox.Focus();
    }

    private async void SaveDieCut_Click(object sender, RoutedEventArgs e)
    {
        if (_api is null) return;
        SaveButton.IsEnabled = false;
        EditorStatus.Text = string.Empty;
        try
        {
            var command = ReadCommand();
            var saved = _editingId is null
                ? await _api.CreateDieCutAsync(command)
                : await _api.UpdateDieCutAsync(_editingId.Value, command);
            _editingId = saved.Id;
            EditorTitle.Text = $"Нож {saved.Number}";
            EditorStatus.Text = "Сохранено";
            await LoadFacetsAsync();
            await LoadPageAsync();
            SelectRow(saved.Id);
        }
        catch (Exception exception) when (exception is CatalogApiException or FormatException)
        {
            EditorStatus.Foreground = System.Windows.Media.Brushes.Firebrick;
            EditorStatus.Text = exception.Message;
        }
        finally
        {
            SaveButton.IsEnabled = true;
            if (string.IsNullOrEmpty(EditorStatus.Text) || EditorStatus.Text == "Сохранено")
                EditorStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 125, 115));
        }
    }

    private SaveDieCutCommand ReadCommand()
    {
        var selectedStatus = StatusBox.SelectedItem as StatusOption ?? _statuses[0];
        return new SaveDieCutCommand(
            NumberBox.Text,
            EquipmentBox.Text,
            ParseDecimal(ShaftBox.Text, "раппорт вала"),
            ParseDecimal(WidthBox.Text, "размер X"),
            ParseDecimal(LengthBox.Text, "размер Y"),
            ParseInt(StreamsBox.Text, "количество ручьёв"),
            ParseInt(RepeatsBox.Text, "количество повторов"),
            ParseDecimal(GapAcrossBox.Text, "зазор по ширине"),
            ParseDecimal(GapAlongBox.Text, "зазор по длине"),
            MaterialBox.Text,
            ParseDecimal(MaterialWidthBox.Text, "ширину материала"),
            ParseOptionalDecimal(KnifeHeightBox.Text, "высоту ножа"),
            ShapeBox.Text,
            string.IsNullOrWhiteSpace(CommentsBox.Text) ? null : CommentsBox.Text,
            CommissionedPicker.SelectedDate is DateTime date ? DateOnly.FromDateTime(date) : null,
            selectedStatus.Value);
    }

    private void FillEditor(DieCutDetails details)
    {
        _editingId = details.Id;
        EditorTitle.Text = $"Нож {details.Number}";
        NumberBox.Text = details.Number;
        EquipmentBox.Text = details.Equipment;
        ShaftBox.Text = Format(details.ShaftRepeatMm);
        WidthBox.Text = Format(details.WidthMm);
        LengthBox.Text = Format(details.LengthMm);
        StreamsBox.Text = details.Streams.ToString(CultureInfo.CurrentCulture);
        RepeatsBox.Text = details.Repeats.ToString(CultureInfo.CurrentCulture);
        GapAcrossBox.Text = Format(details.GapAcrossMm);
        GapAlongBox.Text = Format(details.GapAlongMm);
        MaterialBox.Text = details.Material;
        MaterialWidthBox.Text = Format(details.MaterialWidthMm);
        KnifeHeightBox.Text = details.KnifeHeightMicrons is { } height ? Format(height) : string.Empty;
        ShapeBox.Text = details.Shape;
        CommentsBox.Text = details.Comments;
        CommissionedPicker.SelectedDate = details.CommissionedOn?.ToDateTime(TimeOnly.MinValue);
        StatusBox.SelectedItem = _statuses.First(x => x.Value == details.Status);
        EditorStatus.Text = string.Empty;
    }

    private void ClearEditorFields()
    {
        NumberBox.Clear(); EquipmentBox.Text = string.Empty; ShaftBox.Clear(); WidthBox.Clear(); LengthBox.Clear();
        StreamsBox.Text = "1"; RepeatsBox.Text = "1"; GapAcrossBox.Text = "0"; GapAlongBox.Text = "0";
        MaterialBox.Text = string.Empty; MaterialWidthBox.Clear(); KnifeHeightBox.Clear(); ShapeBox.Text = string.Empty; CommentsBox.Clear();
        CommissionedPicker.SelectedDate = DateTime.Today; StatusBox.SelectedIndex = 0;
    }

    private void OpenEditor()
    {
        EditorColumn.Width = new GridLength(340);
        EditorPanel.Visibility = Visibility.Visible;
    }

    private void CloseEditor_Click(object sender, RoutedEventArgs e) => CloseEditor();

    private void CloseEditor()
    {
        EditorPanel.Visibility = Visibility.Collapsed;
        EditorColumn.Width = new GridLength(0);
        _editingId = null;
        DieCutsGrid.SelectedItem = null;
    }

    private void SelectRow(Guid id)
    {
        var row = _rows.FirstOrDefault(x => x.Id == id);
        if (row is null) return;
        _loadingCard = true;
        DieCutsGrid.SelectedItem = row;
        DieCutsGrid.ScrollIntoView(row);
        _loadingCard = false;
    }

    private void SetNavigationEnabled(bool enabled)
    {
        PreviousPageButton.IsEnabled = enabled;
        NextPageButton.IsEnabled = enabled;
    }

    private static void SetFilterItems(ComboBox comboBox, IReadOnlyList<string> values)
    {
        var selected = comboBox.SelectedItem as string;
        comboBox.ItemsSource = new[] { "Все" }.Concat(values).ToArray();
        comboBox.SelectedItem = selected is not null && comboBox.Items.Contains(selected) ? selected : "Все";
    }

    private static void SetEditorItems(ComboBox comboBox, IReadOnlyList<string> values)
    {
        var text = comboBox.Text;
        comboBox.ItemsSource = values;
        comboBox.Text = text;
    }

    private static string? SelectedFilter(ComboBox comboBox) =>
        comboBox.SelectedItem as string is { } value && value != "Все" ? value : null;

    private static decimal ParseDecimal(string text, string field)
    {
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value)) return value;
        if (decimal.TryParse(text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out value)) return value;
        throw new FormatException($"Проверьте поле «{field}».");
    }

    private static decimal? ParseOptionalDecimal(string text, string field) =>
        string.IsNullOrWhiteSpace(text) ? null : ParseDecimal(text, field);

    private static int ParseInt(string text, string field) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value)
            ? value
            : throw new FormatException($"Проверьте поле «{field}».");

    private static string Format(decimal value) => value.ToString("0.###", CultureInfo.CurrentCulture);
    private string StatusName(DieCutStatus status) => _statuses.First(x => x.Value == status).Name;

    private sealed record StatusOption(DieCutStatus Value, string Name);
    private sealed record DieCutRow(DieCutSummary Source, string StatusText)
    {
        public Guid Id => Source.Id;
        public string Number => Source.Number;
        public string Equipment => Source.Equipment;
        public decimal ShaftRepeatMm => Source.ShaftRepeatMm;
        public decimal WidthMm => Source.WidthMm;
        public decimal LengthMm => Source.LengthMm;
        public int Streams => Source.Streams;
        public int Repeats => Source.Repeats;
        public string Material => Source.Material;
        public decimal MaterialWidthMm => Source.MaterialWidthMm;
        public string Shape => Source.Shape;
    }
}
