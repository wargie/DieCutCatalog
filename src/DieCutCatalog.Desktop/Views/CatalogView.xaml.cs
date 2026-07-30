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
    private static readonly string[] EquipmentOptions =
    [
        "Nilpeter/Lesko",
        "MarkAndy",
        "Big Lesko",
        "Label Source"
    ];
    private static readonly string[] FigureOptions =
    [
        "прямоугольник",
        "круг",
        "квадрат",
        "специальная форма",
        "перфорация"
    ];
    private readonly ObservableCollection<DieCutRow> _rows = [];
    private readonly ObservableCollection<DieCutEventRow> _events = [];
    private readonly IReadOnlyList<StatusOption> _statuses =
    [
        new(DieCutStatus.Active, "ОК"),
        new(DieCutStatus.NeedsInspection, "Требует проверки"),
        new(DieCutStatus.Retired, "Списан"),
        new(DieCutStatus.OrderNew, "Заказать новый")
    ];
    private CatalogApiClient? _api;
    private Guid? _editingId;
    private int _page = 1;
    private int _total;
    private bool _loadingCard;
    private bool _loadingEquipmentTabs;
    private DieCutStatus _loadedStatus = DieCutStatus.Active;
    private string? _pendingPdfPath;
    private DieCutDocumentDetails? _currentDocument;

    public CatalogView()
    {
        InitializeComponent();
        DieCutsGrid.ItemsSource = _rows;
        EventsList.ItemsSource = _events;
        StatusBox.ItemsSource = _statuses;
        EquipmentBox.ItemsSource = EquipmentOptions;
        FigureBox.ItemsSource = FigureOptions;
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
        EquipmentTabs.ItemsSource = null;
        CloseEditor();
        CatalogSummary.Text = "Нет подключения к серверу";
    }

    private async Task LoadFacetsAsync()
    {
        if (_api is null) return;
        var facets = await _api.GetCatalogFacetsAsync();
        var equipment = EquipmentOptions.Concat(facets.Equipment)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        SetEquipmentTabs(equipment);
        SetEditorItems(EquipmentBox, equipment);
        SetFilterItems(MaterialFilter, facets.Materials);
        SetFilterItems(FigureFilter, facets.Figures);
        SetEditorItems(MaterialBox, facets.Materials);
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
                SelectedEquipment(),
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

    private async void EquipmentTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingEquipmentTabs || _api is null) return;
        _page = 1;
        CloseEditor();
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
            FigureBox.SelectedItem = "прямоугольник";
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

    private async void DownloadPdf_Click(object sender, RoutedEventArgs e)
    {
        if (_api is null || _editingId is null || _currentDocument is null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Сохранить PDF-чертёж",
            Filter = "Документ PDF (*.pdf)|*.pdf",
            FileName = _currentDocument.FileName,
            AddExtension = true,
            DefaultExt = ".pdf",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            SetDrawingButtonsEnabled(false);
            var bytes = await _api.DownloadDieCutPdfAsync(_editingId.Value, _currentDocument.Id);
            await File.WriteAllBytesAsync(dialog.FileName, bytes);
            EditorStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(20, 125, 115));
            EditorStatus.Text = "PDF-чертёж сохранён";
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
            var pageBeforeSave = _page;
            var command = ReadCommand();
            var selectedEquipment = SelectedEquipment();
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
            if (selectedEquipment is not null
                && !string.Equals(selectedEquipment, saved.Equipment, StringComparison.OrdinalIgnoreCase))
            {
                SelectEquipmentTab(saved.Equipment);
                _page = 1;
            }
            else
            {
                _page = pageBeforeSave;
            }
            await LoadPageAsync();
            SelectRow(saved.Id);
        }
        catch (Exception exception) when (exception is CatalogApiException or FormatException)
        {
            ShowEditorError(exception.Message);
        }
        finally
        {
            SaveButton.IsEnabled = _loadedStatus is not DieCutStatus.Retired and not DieCutStatus.Deleted;
        }
    }

    private async void AddCirculation_Click(object sender, RoutedEventArgs e)
    {
        if (_api is null || _editingId is null) return;
        try
        {
            long? quantity = null;
            decimal? runLengthMeters = null;
            string successMessage;
            if (QuantityModeRadio.IsChecked == true)
            {
                quantity = ParsePositiveLong(CirculationBox.Text, "тираж");
                successMessage = $"Тираж {quantity.Value:N0} добавлен";
            }
            else
            {
                runLengthMeters = ParsePositiveDecimal(RunLengthInputBox.Text, "пробег");
                successMessage = $"Пробег {runLengthMeters.Value:N2} м добавлен";
            }

            await RunOperationalActionAsync(
                () => _api.AddCirculationAsync(_editingId.Value, quantity, runLengthMeters),
                successMessage);
            CirculationBox.Clear();
            RunLengthInputBox.Clear();
        }
        catch (Exception exception) when (exception is CatalogApiException or FormatException)
        {
            ShowEditorError(exception.Message);
        }
    }

    private void UsageMode_Checked(object sender, RoutedEventArgs e)
    {
        if (CirculationBox is null || RunLengthInputBox is null || AddCirculationButton is null) return;
        var enabled = AddCirculationButton.IsEnabled;
        CirculationBox.IsEnabled = enabled && QuantityModeRadio.IsChecked == true;
        RunLengthInputBox.IsEnabled = enabled && RunLengthModeRadio.IsChecked == true;
        if (QuantityModeRadio.IsChecked == true) RunLengthInputBox.Clear();
        else CirculationBox.Clear();
    }

    private async void InstallReplacement_Click(object sender, RoutedEventArgs e)
    {
        if (_api is null || _editingId is null) return;
        var dialog = new PasswordConfirmationWindow(
            "Установить новый нож",
            $"Подтвердите, что новый физический нож уже установлен. Текущий ресурс ({MileageText.Text} шт, {RunLengthMetersText.Text} м, {RevolutionsText.Text} об.) будет закрыт в журнале. Общий ресурс за всё время сохранится.",
            "Установить новый")
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            await RunOperationalActionAsync(
                () => _api.InstallReplacementAsync(_editingId.Value, dialog.Password),
                "Установка нового ножа зафиксирована");
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

    private async void DeleteDieCut_Click(object sender, RoutedEventArgs e)
    {
        if (_api is null || _editingId is null) return;
        var dialog = new PasswordConfirmationWindow(
            "Удалить нож",
            "Нож будет удалён из каталога. История операций и PDF-чертежи останутся в архиве. Действие подтверждается паролем суперпользователя.",
            "Удалить нож")
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true) return;

        DeleteButton.IsEnabled = false;
        try
        {
            await _api.DeleteDieCutAsync(_editingId.Value, dialog.Password);
            CloseEditor();
            await LoadFacetsAsync();
            _page = 1;
            await LoadPageAsync();
            MessageBox.Show(
                Window.GetWindow(this),
                "Нож удалён из каталога. История и PDF сохранены в архиве.",
                "Каталог ножей",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (CatalogApiException exception)
        {
            ShowEditorError(exception.Message);
            DeleteButton.IsEnabled = true;
        }
    }

    private async Task RunOperationalActionAsync(Func<Task<DieCutDetails>> action, string successMessage)
    {
        SetOperationalButtonsEnabled(false, false);
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
            var operational = StatusBox.SelectedItem is not StatusOption { Value: DieCutStatus.Retired or DieCutStatus.Deleted };
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

        MarkNegativeInput(ShaftBox);
        MarkNegativeInput(XBox);
        MarkNegativeInput(YBox);
        MarkNegativeInput(StreamsBox);
        MarkNegativeInput(RepeatsBox);
        MarkNegativeInput(GrooveSpacingBox);
        MarkNegativeInput(LabelCornerRadiusBox);
        MarkNegativeInput(HBox);

        if (!int.TryParse(ShaftBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var shaft)
            || !TryParseDecimal(XBox.Text, out var x)
            || !TryParseDecimal(YBox.Text, out var y)
            || !int.TryParse(StreamsBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var streams)
            || !int.TryParse(RepeatsBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var repeats)
            || !TryParseDecimal(GrooveSpacingBox.Text, out var grooveSpacing)
            || !TryParseDecimal(HBox.Text, out var h)
            || streams <= 0
            || repeats <= 0)
        {
            GapXBox.Clear();
            GapYBox.Clear();
            UpdateNegativeState(GapXBox, false);
            UpdateNegativeState(GapYBox, false);
            return;
        }

        var (gapX, gapY) = DieCutCalculations.Calculate(
            shaft, x, y, streams, repeats, h, grooveSpacing);
        SetCalculatedValue(GapXBox, gapX);
        SetCalculatedValue(GapYBox, gapY);
    }

    private static void MarkNegativeInput(TextBox box) =>
        UpdateNegativeState(box, TryParseDecimal(box.Text, out var value) && value < 0);

    private static void SetCalculatedValue(TextBox box, decimal value)
    {
        box.Text = FormatGap(value);
        UpdateNegativeState(box, value < 0);
    }

    private static void UpdateNegativeState(TextBox box, bool isNegative)
    {
        if (!isNegative)
        {
            box.ClearValue(Control.ForegroundProperty);
            box.ClearValue(Control.BackgroundProperty);
            box.ClearValue(Control.BorderBrushProperty);
            return;
        }

        box.Foreground = System.Windows.Media.Brushes.Firebrick;
        box.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(255, 238, 236));
        box.BorderBrush = System.Windows.Media.Brushes.Firebrick;
    }

    private SaveDieCutCommand ReadCommand()
    {
        var selectedStatus = StatusBox.SelectedItem as StatusOption ?? _statuses[0];
        if (selectedStatus.Value == DieCutStatus.Retired && _loadedStatus != DieCutStatus.Retired)
        {
            StatusBox.SelectedItem = _statuses.First(x => x.Value == _loadedStatus);
            throw new FormatException("Для списания используйте кнопку «Списать нож» и подтвердите операцию паролем.");
        }

        var shaft = ParseInt(ShaftBox.Text, "shaft — требуется целое число");
        var x = ParseDecimal(XBox.Text, "ширину этикетки L");
        var y = ParseDecimal(YBox.Text, "длину этикетки B");
        var streams = ParseInt(StreamsBox.Text, "количество ручьёв");
        var repeats = ParseInt(RepeatsBox.Text, "количество этикеток в ручье");
        var grooveSpacing = ParseNonNegativeDecimal(GrooveSpacingBox.Text, "расстояние между ручьями");
        var labelCornerRadius = ParseNonNegativeDecimal(LabelCornerRadiusBox.Text, "радиус скругления этикетки");
        var h = ParseDecimal(HBox.Text, "ширину материала");

        if (shaft <= 0 || x <= 0 || y <= 0 || h <= 0)
            throw new FormatException("Вал, L, B и ширина материала должны быть больше нуля.");
        if (streams <= 0 || repeats <= 0)
            throw new FormatException("Количество ручьёв и этикеток в ручье должно быть больше нуля.");
        if (labelCornerRadius > Math.Min(x, y) / 2)
            throw new FormatException("Радиус скругления не может превышать половину меньшей стороны этикетки.");

        var (gapX, gapY) = DieCutCalculations.Calculate(
            shaft, x, y, streams, repeats, h, grooveSpacing);
        if (gapX < 0)
            throw new FormatException(
                $"Недопустимая раскладка: ширина материала не вмещает этикетки и расстояния между ручьями (A1 = {FormatGap(gapX)}).");
        if (gapY < 0)
            throw new FormatException(
                $"Недопустимая раскладка: раппорт не вмещает этикетки в ручье (A2 = {FormatGap(gapY)}).");

        return new SaveDieCutCommand(
            NumberBox.Text,
            string.IsNullOrWhiteSpace(JcOrderNumberBox.Text) ? null : JcOrderNumberBox.Text,
            EquipmentBox.Text,
            shaft,
            x,
            y,
            streams,
            repeats,
            grooveSpacing,
            labelCornerRadius,
            MaterialBox.Text,
            h,
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
        EquipmentBox.SelectedItem = EquipmentBox.Items.Cast<string>().FirstOrDefault(x => string.Equals(x, details.Equipment, StringComparison.OrdinalIgnoreCase));
        ShaftBox.Text = Format(details.Shaft);
        XBox.Text = Format(details.X);
        YBox.Text = Format(details.Y);
        StreamsBox.Text = details.Streams.ToString(CultureInfo.CurrentCulture);
        RepeatsBox.Text = details.Repeats.ToString(CultureInfo.CurrentCulture);
        GrooveSpacingBox.Text = Format(details.GrooveSpacing);
        LabelCornerRadiusBox.Text = Format(details.LabelCornerRadius);
        var (gapX, gapY) = DieCutCalculations.Calculate(
            details.Shaft, details.X, details.Y, details.Streams, details.Repeats,
            details.H, details.GrooveSpacing);
        SetCalculatedValue(GapXBox, gapX);
        SetCalculatedValue(GapYBox, gapY);
        MaterialBox.Text = details.Material;
        HBox.Text = Format(details.H);
        FigureBox.SelectedItem = FigureOptions.FirstOrDefault(x => string.Equals(x, details.Figure, StringComparison.OrdinalIgnoreCase));
        CommentsBox.Text = details.Comments;
        DateBox.SelectedDate = details.Date?.ToDateTime(TimeOnly.MinValue);
        StatusBox.SelectedItem = _statuses.First(x => x.Value == details.Status);
        _loadedStatus = details.Status;
        UpdateStatusBadge(details.Status);
        EditorScrollViewer.ScrollToTop();
        QuantityModeRadio.IsChecked = true;
        CirculationBox.Clear();
        RunLengthInputBox.Clear();
        MileageText.Text = details.Mileage.ToString("N0", CultureInfo.CurrentCulture);
        RunLengthMetersText.Text = FormatRunMetric(details.RunLengthMeters);
        RevolutionsText.Text = details.Revolutions.ToString("N0", CultureInfo.CurrentCulture);
        UpdateResourceIndicators(details);
        OperationsPanel.Visibility = Visibility.Visible;
        EventsSection.Visibility = Visibility.Visible;
        DrawingSection.Visibility = Visibility.Visible;
        SetDrawingButtonsEnabled(true);
        var operational = details.Status is not DieCutStatus.Retired and not DieCutStatus.Deleted;
        SaveButton.IsEnabled = operational;
        StatusBox.IsEnabled = operational;
        SetOperationalButtonsEnabled(operational);
        EditorStatus.Text = operational ? string.Empty : "Нож списан";
    }

    private void ClearEditorFields()
    {
        NumberBox.Clear(); JcOrderNumberBox.Clear(); EquipmentBox.SelectedIndex = 0; ShaftBox.Clear(); XBox.Clear(); YBox.Clear();
        StreamsBox.Text = "1"; RepeatsBox.Text = "1"; GrooveSpacingBox.Text = "0"; LabelCornerRadiusBox.Text = "0";
        GapXBox.Text = "0"; GapYBox.Text = "0";
        MaterialBox.Text = string.Empty; HBox.Clear(); FigureBox.SelectedIndex = 0; CommentsBox.Clear();
        DateBox.SelectedDate = DateTime.Today; StatusBox.SelectedIndex = 0;
        QuantityModeRadio.IsChecked = true; CirculationBox.Clear(); RunLengthInputBox.Clear();
        MileageText.Text = "0"; RunLengthMetersText.Text = FormatRunMetric(0); RevolutionsText.Text = "0";
        ResourceStateText.Text = "Ресурс не начислен"; GenerationText.Text = "Нож №1"; LifetimeUsageText.Text = "Общий ресурс: 0 шт · 0,00 м · 0 об.";
        ResourceProgressBar.Maximum = 1_000_000; ResourceProgressBar.Value = 0;
        StatusBox.IsEnabled = true;
    }

    private void UpdateResourceIndicators(DieCutDetails details)
    {
        var threshold = Math.Max(details.NextInspectionRevolutions, 1_000_000);
        var warningAt = Math.Max(0, threshold - 100_000);
        var requiresInspection = details.Status == DieCutStatus.NeedsInspection || details.Revolutions >= threshold;
        var warning = !requiresInspection && details.Revolutions >= warningAt;
        var color = requiresInspection
            ? System.Windows.Media.Color.FromRgb(180, 35, 24)
            : warning
                ? System.Windows.Media.Color.FromRgb(184, 134, 11)
                : System.Windows.Media.Color.FromRgb(20, 125, 115);

        ResourceProgressBar.Maximum = threshold;
        ResourceProgressBar.Value = Math.Min(details.Revolutions, threshold);
        ResourceProgressBar.Foreground = new System.Windows.Media.SolidColorBrush(color);
        ResourceStateText.Foreground = new System.Windows.Media.SolidColorBrush(color);
        ResourceStateText.Text = details.Status switch
        {
            DieCutStatus.NeedsInspection => "Требуется проверка",
            DieCutStatus.OrderNew => "Ожидается новый нож",
            DieCutStatus.Retired => "Нож списан",
            _ when warning => "Скоро проверка",
            _ => $"До проверки: {Math.Max(0, threshold - details.Revolutions):N0} об."
        };
        GenerationText.Text = $"Физический нож №{details.Generation}";
        LifetimeUsageText.Text = $"За всё время: {details.LifetimeMileage:N0} шт · {FormatRunMetric(details.LifetimeRunLengthMeters)} м · {details.LifetimeRevolutions:N0} об.";
    }
    private void UpdateStatusBadge(DieCutStatus status)
    {
        EditorStatusBadge.Visibility = Visibility.Visible;
        EditorStatusBadgeText.Text = _statuses.First(x => x.Value == status).Name;
        var color = status switch
        {
            DieCutStatus.Active => System.Windows.Media.Color.FromRgb(232, 245, 240),
            DieCutStatus.NeedsInspection => System.Windows.Media.Color.FromRgb(255, 244, 206),
            DieCutStatus.OrderNew => System.Windows.Media.Color.FromRgb(234, 242, 255),
            _ => System.Windows.Media.Color.FromRgb(244, 228, 228)
        };
        var foreground = status switch
        {
            DieCutStatus.Active => System.Windows.Media.Color.FromRgb(20, 125, 115),
            DieCutStatus.NeedsInspection => System.Windows.Media.Color.FromRgb(122, 82, 0),
            DieCutStatus.OrderNew => System.Windows.Media.Color.FromRgb(40, 93, 156),
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

    private void SetOperationalButtonsEnabled(bool enabled, bool enableDelete = true)
    {
        QuantityModeRadio.IsEnabled = enabled;
        RunLengthModeRadio.IsEnabled = enabled;
        AddCirculationButton.IsEnabled = enabled;
        CirculationBox.IsEnabled = enabled && QuantityModeRadio.IsChecked == true;
        RunLengthInputBox.IsEnabled = enabled && RunLengthModeRadio.IsChecked == true;
        InstallReplacementButton.IsEnabled = enabled && _loadedStatus == DieCutStatus.OrderNew;
        RetireButton.IsEnabled = enabled;
        DeleteButton.IsEnabled = enableDelete && _editingId is not null;
    }

    private void SetCurrentDocument(DieCutDocumentDetails? document)
    {
        _currentDocument = document;
        DocumentNameText.Text = document is null
            ? "PDF не прикреплён"
            : $"{DocumentSourceName(document.Source)} · {document.FileName} · {document.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm}";
        OpenPdfButton.IsEnabled = document is not null;
        DownloadPdfButton.IsEnabled = document is not null;
    }

    private void SetDrawingButtonsEnabled(bool enabled)
    {
        UploadPdfButton.IsEnabled = enabled && _editingId is not null;
        GeneratePdfButton.IsEnabled = enabled && _editingId is not null;
        OpenPdfButton.IsEnabled = enabled && _currentDocument is not null;
        DownloadPdfButton.IsEnabled = enabled && _currentDocument is not null;
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
        DownloadPdfButton.IsEnabled = false;
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

    private void SetEquipmentTabs(IReadOnlyList<string> values)
    {
        const string allEquipment = "Все ножи";
        var selected = EquipmentTabs.SelectedItem as string ?? allEquipment;
        var items = new[] { allEquipment }.Concat(values).ToArray();

        _loadingEquipmentTabs = true;
        EquipmentTabs.ItemsSource = items;
        EquipmentTabs.SelectedItem = items.Contains(selected) ? selected : allEquipment;
        _loadingEquipmentTabs = false;
    }

    private string? SelectedEquipment() =>
        EquipmentTabs.SelectedIndex > 0 ? EquipmentTabs.SelectedItem as string : null;

    private void SelectEquipmentTab(string equipment)
    {
        if (!EquipmentTabs.Items.Contains(equipment)) return;
        _loadingEquipmentTabs = true;
        EquipmentTabs.SelectedItem = equipment;
        _loadingEquipmentTabs = false;
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

    private static decimal ParsePositiveDecimal(string text, string field)
    {
        var value = ParseDecimal(text, field);
        return value > 0
            ? value
            : throw new FormatException($"Поле «{field}» должно содержать число больше нуля.");
    }

    private static decimal ParseNonNegativeDecimal(string text, string field)
    {
        var value = ParseDecimal(text, field);
        return value >= 0
            ? value
            : throw new FormatException($"Поле «{field}» не может быть отрицательным.");
    }

    private static string Format(decimal value) => value.ToString("0.###", CultureInfo.CurrentCulture);
    private static string FormatGap(decimal value) => value.ToString("0.#####", CultureInfo.CurrentCulture);
    private static string FormatTableGap(decimal value) => value.ToString("0.000", CultureInfo.CurrentCulture);
    private static string FormatRunMetric(decimal value) => value.ToString("N2", CultureInfo.CurrentCulture);

    private static string StatusName(DieCutStatus status) => status switch
    {
        DieCutStatus.Active => "ОК",
        DieCutStatus.NeedsInspection => "Требует проверки",
        DieCutStatus.Retired => "Списан",
        DieCutStatus.OrderNew => "Заказать новый",
        _ => "Удалён"
    };

    private sealed record StatusOption(DieCutStatus Value, string Name);

    private sealed record DieCutRow(DieCutSummary Source)
    {
        public Guid Id => Source.Id;
        public string Number => Source.Number;
        public string StatusText => StatusName(Source.Status);
        public System.Windows.Media.Brush RowBackground
        {
            get
            {
                var threshold = Math.Max(Source.NextInspectionRevolutions, 1_000_000);
                var color = Source.Status switch
                {
                    DieCutStatus.Retired => System.Windows.Media.Color.FromRgb(245, 245, 245),
                    DieCutStatus.OrderNew => System.Windows.Media.Color.FromRgb(236, 244, 255),
                    DieCutStatus.NeedsInspection => System.Windows.Media.Color.FromRgb(255, 232, 230),
                    _ when Source.Revolutions >= threshold => System.Windows.Media.Color.FromRgb(255, 232, 230),
                    _ when Source.Revolutions >= threshold - 100_000 => System.Windows.Media.Color.FromRgb(255, 248, 218),
                    _ => System.Windows.Media.Color.FromRgb(237, 249, 244)
                };
                return new System.Windows.Media.SolidColorBrush(color);
            }
        }
        public string? JcOrderNumber => Source.JcOrderNumber;
        public string MileageText => Source.Mileage.ToString("N0", CultureInfo.CurrentCulture);
        public string RunLengthMetersText => FormatRunMetric(Source.RunLengthMeters);
        public string RevolutionsText => Source.Revolutions.ToString("N0", CultureInfo.CurrentCulture);
        public string Equipment => Source.Equipment;
        public int Shaft => Source.Shaft;
        public string XText => Format(Source.X);
        public string YText => Format(Source.Y);
        public int Streams => Source.Streams;
        public int Repeats => Source.Repeats;
        public string GapXText => FormatTableGap(Source.GapX);
        public string GapYText => FormatTableGap(Source.GapY);
        public bool IsGapXNegative => Source.GapX < 0;
        public bool IsGapYNegative => Source.GapY < 0;
        public string Material => Source.Material;
        public string HText => Format(Source.H);
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
                DieCutEventType.ReplacementInstalled =>
                    $"Установлен новый нож · ресурс заменённого ножа {source.MileageBefore:N0} шт · {source.RunLengthMetersBefore:N2} м · {source.RevolutionsBefore:N0} об.",
                DieCutEventType.Retired =>
                    $"Нож списан · тираж {source.MileageAfter:N0} шт · {source.RunLengthMetersAfter:N2} м · {source.RevolutionsAfter:N0} об.",
                DieCutEventType.Deleted =>
                    "Нож удалён из каталога",
                DieCutEventType.Created =>
                    "Создана карточка ножа",
                DieCutEventType.Updated =>
                    "Изменены параметры ножа",
                DieCutEventType.DrawingGenerated =>
                    "Сформирован PDF-чертёж ножа",
                _ => "Изменение ножа"
            };
        }
    }
}