using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
    private readonly ObservableCollection<DieCutEventRow> _events = [];
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
    private DieCutStatus _loadedStatus = DieCutStatus.Active;
    private string? _pendingPdfPath;
    private DieCutDocumentDetails? _currentDocument;

    public CatalogView()
    {
        InitializeComponent();
        DieCutsGrid.ItemsSource = _rows;
        EventsList.ItemsSource = _events;
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
        _events.Clear();
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

    private async Task LoadEventsAsync(Guid id)
    {
        if (_api is null) return;
        SetEvents(await _api.GetDieCutEventsAsync(id));
    }

    private async Task LoadDocumentsAsync(Guid id)
    {
        if (_api is null) return;
        var documents = await _api.GetDieCutDocumentsAsync(id);
        SetCurrentDocument(documents.FirstOrDefault());
        DrawingSection.Visibility = Visibility.Visible;
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
            var detailsTask = _api.GetDieCutAsync(row.Id);
            var eventsTask = _api.GetDieCutEventsAsync(row.Id);
            var documentsTask = _api.GetDieCutDocumentsAsync(row.Id);
            await Task.WhenAll(detailsTask, eventsTask, documentsTask);
            FillEditor(await detailsTask);
            SetEvents(await eventsTask);
            SetCurrentDocument((await documentsTask).FirstOrDefault());
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

    private async void ImportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (_api is null) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите схему ножа в PDF",
            Filter = "Документ PDF (*.pdf)|*.pdf",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        ImportPdfButton.IsEnabled = false;
        CatalogError.Text = string.Empty;
        try
        {
            var preview = await _api.PreviewPdfImportAsync(dialog.FileName);
            BeginNewDieCut();
            _pendingPdfPath = dialog.FileName;
            DrawingSection.Visibility = Visibility.Visible;
            DocumentNameText.Text = $"Будет прикреплён · {Path.GetFileName(dialog.FileName)}";
            NumberBox.Text = preview.Number ?? string.Empty;
            ShaftBox.Text = preview.Shaft?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            XBox.Text = preview.LabelWidth is null ? string.Empty : Format(preview.LabelWidth.Value);
            YBox.Text = preview.LabelLength is null ? string.Empty : Format(preview.LabelLength.Value);
            StreamsBox.Text = preview.Streams?.ToString(CultureInfo.CurrentCulture) ?? "1";
            RepeatsBox.Text = preview.Repeats?.ToString(CultureInfo.CurrentCulture) ?? "1";
            GrooveSpacingBox.Text = preview.GrooveSpacing is null ? "0" : Format(preview.GrooveSpacing.Value);
            LabelCornerRadiusBox.Text = preview.LabelCornerRadius is null ? "0" : Format(preview.LabelCornerRadius.Value);
            MaterialBox.Text = preview.Material ?? string.Empty;
            HBox.Text = preview.MaterialWidth is null ? string.Empty : Format(preview.MaterialWidth.Value);
            FigureBox.Text = "прямоугольник";
            EditorStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(49, 94, 145));
            EditorStatus.Text = preview.Warnings.Count == 0
                ? "Данные распознаны. Проверьте карточку и сохраните нож."
                : $"Проверьте карточку: {string.Join(" ", preview.Warnings)}";
            EquipmentBox.Focus();
        }
        catch (CatalogApiException exception)
        {
            CatalogError.Text = exception.Message;
        }
        finally
        {
            ImportPdfButton.IsEnabled = true;
        }
    }

    private async void UploadPdf_Click(object sender, RoutedEventArgs e)
    {
        if (_api is null || _editingId is null) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Прикрепить схему ножа",
            Filter = "Документ PDF (*.pdf)|*.pdf",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            SetDrawingButtonsEnabled(false);
            var document = await _api.UploadDieCutPdfAsync(_editingId.Value, dialog.FileName);
            SetCurrentDocument(document);
            EditorStatus.Text = "PDF прикреплён";
        }
        catch (CatalogApiException exception)
        {
            ShowEditorError(exception.Message);
        }
        finally
        {
            SetDrawingButtonsEnabled(true);
        }
    }

    private async void GeneratePdf_Click(object sender, RoutedEventArgs e)
    {
        if (_api is null || _editingId is null) return;
        try
        {
            SetDrawingButtonsEnabled(false);
            var document = await _api.GenerateDieCutPdfAsync(_editingId.Value);
            SetCurrentDocument(document);
            await LoadEventsAsync(_editingId.Value);
            EditorStatus.Text = "Чертёж сформирован";
            await OpenDocumentAsync(document);
        }
        catch (Exception exception) when (exception is CatalogApiException or IOException)
        {
            ShowEditorError(exception.Message);
        }
        finally
        {
            SetDrawingButtonsEnabled(true);
        }
    }

    private async void OpenPdf_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDocument is null) return;
        try
        {
            SetDrawingButtonsEnabled(false);
            await OpenDocumentAsync(_currentDocument);
        }
        catch (Exception exception) when (exception is CatalogApiException or IOException)
        {
            ShowEditorError(exception.Message);
        }
        finally
        {
            SetDrawingButtonsEnabled(true);
        }
    }

    private async Task OpenDocumentAsync(DieCutDocumentDetails document)
    {
        if (_api is null || _editingId is null) return;
        var bytes = await _api.DownloadDieCutPdfAsync(_editingId.Value, document.Id);
        var directory = Path.Combine(Path.GetTempPath(), "DieCutCatalog");
        Directory.CreateDirectory(directory);
        var fileName = $"{document.Id:N}_{Path.GetFileName(document.FileName)}";
        var path = Path.Combine(directory, fileName);
        await File.WriteAllBytesAsync(path, bytes);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void NewDieCut_Click(object sender, RoutedEventArgs e) => BeginNewDieCut();

    private void BeginNewDieCut()
    {
        _editingId = null;
        _pendingPdfPath = null;
        _currentDocument = null;
        DieCutsGrid.SelectedItem = null;
        _events.Clear();
        ClearEditorFields();
        OperationsPanel.Visibility = Visibility.Collapsed;
        EventsSection.Visibility = Visibility.Collapsed;
        DrawingSection.Visibility = Visibility.Collapsed;
        EditorStatusBadge.Visibility = Visibility.Collapsed;
        EditorTitle.Text = "Новый нож";
        EditorStatus.Text = string.Empty;
        SaveButton.IsEnabled = true;
        StatusBox.IsEnabled = true;
        _loadedStatus = DieCutStatus.Active;
        EditorScrollViewer.ScrollToTop();
        OpenEditor();
        SetDrawingButtonsEnabled(false);
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
            FillEditor(saved);
            await LoadEventsAsync(saved.Id);
            if (_pendingPdfPath is not null)
            {
                await _api.UploadDieCutPdfAsync(saved.Id, _pendingPdfPath);
                _pendingPdfPath = null;
            }
            await LoadDocumentsAsync(saved.Id);
            EditorStatus.Text = "Сохранено";
            await LoadFacetsAsync();
            await LoadPageAsync();
            SelectRow(saved.Id);
        }
        catch (Exception exception) when (exception is CatalogApiException or FormatException)
        {
            ShowEditorError(exception.Message);
        }
        finally
        {
            SaveButton.IsEnabled = _loadedStatus != DieCutStatus.Retired;
        }
    }

    private async void AddCirculation_Click(object sender, RoutedEventArgs e)
    {
        if (_api is null || _editingId is null) return;
        try
        {
            var quantity = ParsePositiveLong(CirculationBox.Text, "тираж");
            await RunOperationalActionAsync(
                () => _api.AddCirculationAsync(_editingId.Value, quantity),
                $"Тираж {quantity:N0} добавлен");
            CirculationBox.Clear();
        }
        catch (Exception exception) when (exception is CatalogApiException or FormatException)
        {
            ShowEditorError(exception.Message);
        }
    }

    private async void ResetMileage_Click(object sender, RoutedEventArgs e)
    {
        if (_api is null || _editingId is null) return;
        var dialog = new PasswordConfirmationWindow(
            "Сбросить тираж",
            $"Суммарный тираж {MileageText.Text} шт, пробег {RunLengthMetersText.Text} м, обороты {RevolutionsText.Text}. После подтверждения все три счётчика станут равны нулю.",
            "Сбросить")
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            await RunOperationalActionAsync(
                () => _api.ResetMileageAsync(_editingId.Value, dialog.Password),
                "Счётчики тиража сброшены");
        }
        catch (CatalogApiException exception)
        {
            ShowEditorError(exception.Message);
        }
    }

    private async void RetireDieCut_Click(object sender, RoutedEventArgs e)
    {
        if (_api is null || _editingId is null) return;
        var dialog = new PasswordConfirmationWindow(
            "Списать нож",
            "Нож получит статус «Списан». Добавление тиражей и редактирование карточки будут заблокированы. Дата и сотрудник сохранятся в журнале.",
            "Списать нож")
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            await RunOperationalActionAsync(
                () => _api.RetireDieCutAsync(_editingId.Value, dialog.Password),
                "Нож списан");
        }
        catch (CatalogApiException exception)
        {
            ShowEditorError(exception.Message);
        }
    }

    private async Task RunOperationalActionAsync(Func<Task<DieCutDetails>> action, string successMessage)
    {
        SetOperationalButtonsEnabled(false);
        EditorStatus.Text = string.Empty;
        try
        {
            var updated = await action();
            FillEditor(updated);
            await LoadEventsAsync(updated.Id);
            await LoadPageAsync();
            SelectRow(updated.Id);
            EditorStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(20, 125, 115));
            EditorStatus.Text = successMessage;
        }
        finally
        {
            var operational = StatusBox.SelectedItem is not StatusOption { Value: DieCutStatus.Retired };
            SetOperationalButtonsEnabled(operational);
        }
    }

    private void ShaftBox_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = e.Text.Any(character => !char.IsDigit(character));

    private void PositiveInteger_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
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
        if (selectedStatus.Value == DieCutStatus.Retired && _loadedStatus != DieCutStatus.Retired)
        {
            StatusBox.SelectedItem = _statuses.First(x => x.Value == _loadedStatus);
            throw new FormatException("Для списания используйте кнопку «Списать нож» и подтвердите операцию паролем.");
        }

        return new SaveDieCutCommand(
            NumberBox.Text,
            string.IsNullOrWhiteSpace(JcOrderNumberBox.Text) ? null : JcOrderNumberBox.Text,
            EquipmentBox.Text,
            ParseInt(ShaftBox.Text, "shaft — требуется целое число"),
            ParseDecimal(XBox.Text, "ширину этикетки L"),
            ParseDecimal(YBox.Text, "длину этикетки B"),
            ParseInt(StreamsBox.Text, "количество ручьёв"),
            ParseInt(RepeatsBox.Text, "количество этикеток в ручье"),
            ParseNonNegativeDecimal(GrooveSpacingBox.Text, "расстояние между ручьями"),
            ParseNonNegativeDecimal(LabelCornerRadiusBox.Text, "радиус скругления этикетки"),
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
        JcOrderNumberBox.Text = details.JcOrderNumber;
        EquipmentBox.Text = details.Equipment;
        ShaftBox.Text = Format(details.Shaft);
        XBox.Text = Format(details.X);
        YBox.Text = Format(details.Y);
        StreamsBox.Text = details.Streams.ToString(CultureInfo.CurrentCulture);
        RepeatsBox.Text = details.Repeats.ToString(CultureInfo.CurrentCulture);
        GrooveSpacingBox.Text = Format(details.GrooveSpacing);
        LabelCornerRadiusBox.Text = Format(details.LabelCornerRadius);
        GapXBox.Text = Format(details.GapX);
        GapYBox.Text = Format(details.GapY);
        MaterialBox.Text = details.Material;
        HBox.Text = Format(details.H);
        FigureBox.Text = details.Figure;
        CommentsBox.Text = details.Comments;
        DateBox.SelectedDate = details.Date?.ToDateTime(TimeOnly.MinValue);
        StatusBox.SelectedItem = _statuses.First(x => x.Value == details.Status);
        _loadedStatus = details.Status;
        UpdateStatusBadge(details.Status);
        EditorScrollViewer.ScrollToTop();
        MileageText.Text = details.Mileage.ToString("N0", CultureInfo.CurrentCulture);
        RunLengthMetersText.Text = FormatRunMetric(details.RunLengthMeters);
        RevolutionsText.Text = details.Revolutions.ToString("N0", CultureInfo.CurrentCulture);
        OperationsPanel.Visibility = Visibility.Visible;
        EventsSection.Visibility = Visibility.Visible;
        DrawingSection.Visibility = Visibility.Visible;
        SetDrawingButtonsEnabled(true);
        var operational = details.Status != DieCutStatus.Retired;
        SaveButton.IsEnabled = operational;
        StatusBox.IsEnabled = operational;
        SetOperationalButtonsEnabled(operational);
        EditorStatus.Text = operational ? string.Empty : "Нож списан";
    }

    private void ClearEditorFields()
    {
        NumberBox.Clear(); JcOrderNumberBox.Clear(); EquipmentBox.Text = string.Empty; ShaftBox.Clear(); XBox.Clear(); YBox.Clear();
        StreamsBox.Text = "1"; RepeatsBox.Text = "1"; GrooveSpacingBox.Text = "0"; LabelCornerRadiusBox.Text = "0";
        GapXBox.Text = "0"; GapYBox.Text = "0";
        MaterialBox.Text = string.Empty; HBox.Clear(); FigureBox.Text = string.Empty; CommentsBox.Clear();
        DateBox.SelectedDate = DateTime.Today; StatusBox.SelectedIndex = 0; CirculationBox.Clear();
        MileageText.Text = "0"; RunLengthMetersText.Text = FormatRunMetric(0); RevolutionsText.Text = "0";
        StatusBox.IsEnabled = true;
    }

    private void UpdateStatusBadge(DieCutStatus status)
    {
        EditorStatusBadge.Visibility = Visibility.Visible;
        EditorStatusBadgeText.Text = _statuses.First(x => x.Value == status).Name;
        var color = status switch
        {
            DieCutStatus.Active => System.Windows.Media.Color.FromRgb(231, 241, 255),
            DieCutStatus.NeedsInspection => System.Windows.Media.Color.FromRgb(255, 244, 206),
            _ => System.Windows.Media.Color.FromRgb(244, 228, 228)
        };
        var foreground = status switch
        {
            DieCutStatus.Active => System.Windows.Media.Color.FromRgb(23, 105, 170),
            DieCutStatus.NeedsInspection => System.Windows.Media.Color.FromRgb(122, 82, 0),
            _ => System.Windows.Media.Color.FromRgb(180, 35, 24)
        };
        EditorStatusBadge.Background = new System.Windows.Media.SolidColorBrush(color);
        EditorStatusBadgeText.Foreground = new System.Windows.Media.SolidColorBrush(foreground);
    }
    private void SetEvents(IReadOnlyList<DieCutEventDetails> events)
    {
        _events.Clear();
        foreach (var item in events) _events.Add(new DieCutEventRow(item));
    }

    private void SetOperationalButtonsEnabled(bool enabled)
    {
        CirculationBox.IsEnabled = enabled;
        AddCirculationButton.IsEnabled = enabled;
        ResetMileageButton.IsEnabled = enabled;
        RetireButton.IsEnabled = enabled;
    }

    private void SetCurrentDocument(DieCutDocumentDetails? document)
    {
        _currentDocument = document;
        DocumentNameText.Text = document is null
            ? "PDF не прикреплён"
            : $"{DocumentSourceName(document.Source)} · {document.FileName} · {document.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm}";
        OpenPdfButton.IsEnabled = document is not null;
    }

    private void SetDrawingButtonsEnabled(bool enabled)
    {
        UploadPdfButton.IsEnabled = enabled && _editingId is not null;
        GeneratePdfButton.IsEnabled = enabled && _editingId is not null;
        OpenPdfButton.IsEnabled = enabled && _currentDocument is not null;
    }

    private static string DocumentSourceName(DieCutDocumentSource source) =>
        source == DieCutDocumentSource.Generated ? "Сформирован" : "Загружен";
    private void ShowEditorError(string message)
    {
        EditorStatus.Foreground = System.Windows.Media.Brushes.Firebrick;
        EditorStatus.Text = message;
    }

    private void OpenEditor()
    {
        EditorColumn.Width = new GridLength(510);
        EditorPanel.Visibility = Visibility.Visible;
    }

    private void CloseEditor_Click(object sender, RoutedEventArgs e) => CloseEditor();

    private void CloseEditor()
    {
        EditorPanel.Visibility = Visibility.Collapsed;
        OperationsPanel.Visibility = Visibility.Collapsed;
        EventsSection.Visibility = Visibility.Collapsed;
        DrawingSection.Visibility = Visibility.Collapsed;
        EditorStatusBadge.Visibility = Visibility.Collapsed;
        EditorColumn.Width = new GridLength(0);
        _editingId = null;
        _pendingPdfPath = null;
        _currentDocument = null;
        _events.Clear();
        DocumentNameText.Text = "PDF не прикреплён";
        OpenPdfButton.IsEnabled = false;
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

    private static long ParsePositiveLong(string text, string field) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) && value > 0
            ? value
            : throw new FormatException($"Поле «{field}» должно содержать целое число больше нуля.");

    private static decimal ParseNonNegativeDecimal(string text, string field)
    {
        var value = ParseDecimal(text, field);
        return value >= 0
            ? value
            : throw new FormatException($"Поле «{field}» не может быть отрицательным.");
    }

    private static string Format(decimal value) => value.ToString("0.###", CultureInfo.CurrentCulture);
    private static string FormatGap(decimal value) => value.ToString("0.#########", CultureInfo.CurrentCulture);
    private static string FormatRunMetric(decimal value) => value.ToString("N2", CultureInfo.CurrentCulture);

    private sealed record StatusOption(DieCutStatus Value, string Name);

    private sealed record DieCutRow(DieCutSummary Source)
    {
        public Guid Id => Source.Id;
        public string Number => Source.Number;
        public string? JcOrderNumber => Source.JcOrderNumber;
        public string MileageText => Source.Mileage.ToString("N0", CultureInfo.CurrentCulture);
        public string RunLengthMetersText => FormatRunMetric(Source.RunLengthMeters);
        public string RevolutionsText => Source.Revolutions.ToString("N0", CultureInfo.CurrentCulture);
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

    private sealed record DieCutEventRow
    {
        public string DateText { get; }
        public string Description { get; }
        public string EmployeeName { get; }

        public DieCutEventRow(DieCutEventDetails source)
        {
            DateText = source.OccurredAt.ToLocalTime().ToString("dd.MM.yy HH:mm", CultureInfo.CurrentCulture);
            EmployeeName = string.IsNullOrWhiteSpace(source.EmployeeName) ? "Сотрудник" : source.EmployeeName;
            Description = source.Type switch
            {
                DieCutEventType.CirculationAdded =>
                    $"Добавлен тираж +{source.Quantity.GetValueOrDefault():N0} · итог {source.MileageAfter:N0} шт · {source.RunLengthMetersAfter:N2} м · {source.RevolutionsAfter:N0} об.",
                DieCutEventType.MileageReset =>
                    $"Счётчики сброшены · было {source.MileageBefore:N0} шт · {source.RunLengthMetersBefore:N2} м · {source.RevolutionsBefore:N0} об.",
                DieCutEventType.Retired =>
                    $"Нож списан · тираж {source.MileageAfter:N0} шт · {source.RunLengthMetersAfter:N2} м · {source.RevolutionsAfter:N0} об.",
                DieCutEventType.Created =>
                    "Создана карточка ножа",
                DieCutEventType.DrawingGenerated =>
                    "Сформирован PDF-чертёж ножа",
                _ => "Изменение ножа"
            };
        }
    }
}