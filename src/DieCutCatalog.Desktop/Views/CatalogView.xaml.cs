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
        SetFilterItems(FigureFilter, facets.Figures);
        SetEditorItems(EquipmentBox, facets.Equipment);
        SetEditorItems(MaterialBox, facets.Materials);
        SetEditorItems(FigureBox, facets.Figures);
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
                SelectedFilter(FigureFilter),
                _page,
                PageSize);
            _total = result.Total;
            _rows.Clear();
            foreach (var item in result.Items) _rows.Add(new DieCutRow(item));

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

    private void ShaftBox_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = e.Text.Any(character => !char.IsDigit(character));

    private void CalculationInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (GapXBox is null || GapYBox is null) return;
        if (!int.TryParse(ShaftBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var shaft)
            || !TryParseDecimal(XBox.Text, out var x)
            || !TryParseDecimal(YBox.Text, out var y)
            || !int.TryParse(StreamsBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var streams)
            || !int.TryParse(RepeatsBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var repeats)
            || !TryParseDecimal(HBox.Text, out var h)
            || streams <= 0
            || repeats <= 0)
        {
            GapXBox.Clear();
            GapYBox.Clear();
            return;
        }

        var (gapX, gapY) = DieCutCalculations.Calculate(shaft, x, y, streams, repeats, h);
        GapXBox.Text = FormatGap(gapX);
        GapYBox.Text = FormatGap(gapY);
    }
    private SaveDieCutCommand ReadCommand()
    {
        var selectedStatus = StatusBox.SelectedItem as StatusOption ?? _statuses[0];
        return new SaveDieCutCommand(
            NumberBox.Text,
            EquipmentBox.Text,
            ParseInt(ShaftBox.Text, "shaft — требуется целое число"),
            ParseDecimal(XBox.Text, "размер X"),
            ParseDecimal(YBox.Text, "размер Y"),
            ParseInt(StreamsBox.Text, "количество ручьёв"),
            ParseInt(RepeatsBox.Text, "количество повторов"),
            MaterialBox.Text,
            ParseDecimal(HBox.Text, "ширину материала"),
            FigureBox.Text,
            string.IsNullOrWhiteSpace(CommentsBox.Text) ? null : CommentsBox.Text,
            DateBox.SelectedDate is DateTime date ? DateOnly.FromDateTime(date) : null,
            selectedStatus.Value);
    }

    private void FillEditor(DieCutDetails details)
    {
        _editingId = details.Id;
        EditorTitle.Text = $"Нож {details.Number}";
        NumberBox.Text = details.Number;
        EquipmentBox.Text = details.Equipment;
        ShaftBox.Text = Format(details.Shaft);
        XBox.Text = Format(details.X);
        YBox.Text = Format(details.Y);
        StreamsBox.Text = details.Streams.ToString(CultureInfo.CurrentCulture);
        RepeatsBox.Text = details.Repeats.ToString(CultureInfo.CurrentCulture);
        GapXBox.Text = Format(details.GapX);
        GapYBox.Text = Format(details.GapY);
        MaterialBox.Text = details.Material;
        HBox.Text = Format(details.H);
        FigureBox.Text = details.Figure;
        CommentsBox.Text = details.Comments;
        DateBox.SelectedDate = details.Date?.ToDateTime(TimeOnly.MinValue);
        StatusBox.SelectedItem = _statuses.First(x => x.Value == details.Status);
        EditorStatus.Text = string.Empty;
    }

    private void ClearEditorFields()
    {
        NumberBox.Clear(); EquipmentBox.Text = string.Empty; ShaftBox.Clear(); XBox.Clear(); YBox.Clear();
        StreamsBox.Text = "1"; RepeatsBox.Text = "1"; GapXBox.Text = "0"; GapYBox.Text = "0";
        MaterialBox.Text = string.Empty; HBox.Clear(); FigureBox.Text = string.Empty; CommentsBox.Clear();
        DateBox.SelectedDate = DateTime.Today; StatusBox.SelectedIndex = 0;
    }

    private void OpenEditor()
    {
        EditorColumn.Width = new GridLength(400);
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

    private static bool TryParseDecimal(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value)
        || decimal.TryParse(text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    private static decimal ParseDecimal(string text, string field)
    {
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value)) return value;
        if (decimal.TryParse(text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out value)) return value;
        throw new FormatException($"Проверьте поле «{field}».");
    }

    private static int ParseInt(string text, string field) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value)
            ? value
            : throw new FormatException($"Проверьте поле «{field}».");

    private static string Format(decimal value) => value.ToString("0.###", CultureInfo.CurrentCulture);
    private static string FormatGap(decimal value) => value.ToString("0.#########", CultureInfo.CurrentCulture);
    private string StatusName(DieCutStatus status) => _statuses.First(x => x.Value == status).Name;

    private sealed record StatusOption(DieCutStatus Value, string Name);
    private sealed record DieCutRow(DieCutSummary Source)
    {
        public Guid Id => Source.Id;
        public string Number => Source.Number;
        public string Equipment => Source.Equipment;
        public int Shaft => Source.Shaft;
        public decimal X => Source.X;
        public decimal Y => Source.Y;
        public int Streams => Source.Streams;
        public int Repeats => Source.Repeats;
        public string GapXText => FormatGap(Source.GapX);
        public string GapYText => FormatGap(Source.GapY);
        public string Material => Source.Material;
        public decimal H => Source.H;
        public string Figure => Source.Figure;
        public string? Comments => Source.Comments;
        public string DateText => Source.Date?.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture) ?? string.Empty;
    }
}
