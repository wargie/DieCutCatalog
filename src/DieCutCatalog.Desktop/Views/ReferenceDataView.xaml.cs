using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DieCutCatalog.Application.Catalog;
using DieCutCatalog.Domain.Catalog;
using DieCutCatalog.Domain.Employees;
using Microsoft.Win32;

namespace DieCutCatalog.Desktop.Views;

public partial class ReferenceDataView : UserControl
{
    private readonly ObservableCollection<CatalogReferenceItem> _materials = [];
    private readonly ObservableCollection<CatalogReferenceItem> _figures = [];
    private readonly ObservableCollection<CatalogReferenceItem> _equipment = [];
    private readonly ObservableCollection<AuditLogRow> _audit = [];
    private readonly ObservableCollection<DirectoryRow> _directories = [];
    private readonly ObservableCollection<ReferenceDirectoryGroupItem> _directoryGroups = [];
    private readonly ObservableCollection<DirectoryValueRow> _directoryValues = [];
    private IReadOnlyList<ReferenceDirectoryValueItem> _allDirectoryValues = [];
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
        var directoryView = CollectionViewSource.GetDefaultView(_directories);
        directoryView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DirectoryRow.GroupName)));
        DirectoriesList.ItemsSource = directoryView;
        DirectoryGroupBox.ItemsSource = _directoryGroups;
        DirectoryValuesGrid.ItemsSource = _directoryValues;
        ConfigurePositionMenu(MaterialsGrid);
        ConfigurePositionMenu(FiguresGrid);
        ConfigurePositionMenu(EquipmentGrid);
        ConfigurePositionMenu(DirectoryValuesGrid);
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
            UpdateReferenceCounts();
            await LoadDirectoriesAsync();
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
        _directories.Clear();
        _directoryGroups.Clear();
        _directoryValues.Clear();
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

    private async Task LoadDirectoriesAsync(Guid? selectId = null)
    {
        if (_api is null) return;
        var overview = await _api.GetReferenceDirectoryOverviewAsync();
        _directoryGroups.Clear();
        foreach (var group in overview.Groups) _directoryGroups.Add(group);
        var activeDirectories = overview.Directories.Where(x => !x.IsArchived).ToList();
        _directories.Clear();
        foreach (var directory in activeDirectories
                     .OrderBy(x => GroupName(x.GroupId)).ThenBy(x => x.SortOrder).ThenBy(x => x.Name))
            _directories.Add(new DirectoryRow(directory, GroupName(directory.GroupId)));
        foreach (var group in overview.Groups.Where(group => activeDirectories.All(x => x.GroupId != group.Id)))
            _directories.Add(new DirectoryRow(group));
        DirectoriesCountText.Text = activeDirectories.Count.ToString(CultureInfo.InvariantCulture);
        var selected = _directories.FirstOrDefault(x => !x.IsPlaceholder && x.Id == selectId)
            ?? DirectoriesList.SelectedItem as DirectoryRow;
        if (selected?.IsPlaceholder == true) selected = null;
        DirectoriesList.SelectedItem = selected;
        if (selected is not null) await LoadDirectoryValuesAsync(selected);
        else
        {
            ClearDirectorySelection();
            ShowReferencePanel(MaterialsPanel, MaterialsNavButton);
        }
    }

    private string GroupName(Guid? id) => id is null
        ? "Без группы"
        : _directoryGroups.FirstOrDefault(x => x.Id == id)?.Name ?? "Без группы";

    private async Task LoadDirectoryValuesAsync(DirectoryRow directory)
    {
        if (_api is null) return;
        SelectedDirectoryTitle.Text = directory.Name;
        SelectedDirectoryDescription.Text = $"{directory.GroupName} · значений: {directory.ValueCount}";
        ShowReferencePanel(CustomDirectoryPanel, null);
        _allDirectoryValues = await _api.GetReferenceDirectoryValuesAsync(directory.Id);
        ApplyDirectoryValueFilter();
    }

    private void ApplyDirectoryValueFilter()
    {
        var term = DirectoryValueSearchBox.Text.Trim();
        _directoryValues.Clear();
        foreach (var value in _allDirectoryValues.Where(x => term.Length == 0 || x.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)))
            _directoryValues.Add(new DirectoryValueRow(value));
    }

    private void ClearDirectorySelection()
    {
        SelectedDirectoryTitle.Text = "Выберите справочник";
        SelectedDirectoryDescription.Text = "Создайте справочник слева или выберите существующий.";
        _allDirectoryValues = [];
        _directoryValues.Clear();
    }

    private async void ReloadDirectories_Click(object sender, RoutedEventArgs e) => await RunAsync(() => LoadDirectoriesAsync());

    private void NewDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        CreateDirectoryPanel.Visibility = CreateDirectoryPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (CreateDirectoryPanel.Visibility == Visibility.Visible) NewDirectoryNameBox.Focus();
    }

    private void ShowMaterials_Click(object sender, RoutedEventArgs e)
    {
        DirectoriesList.SelectedItem = null;
        ShowReferencePanel(MaterialsPanel, MaterialsNavButton);
    }

    private void ShowFigures_Click(object sender, RoutedEventArgs e)
    {
        DirectoriesList.SelectedItem = null;
        ShowReferencePanel(FiguresPanel, FiguresNavButton);
    }

    private void ShowEquipment_Click(object sender, RoutedEventArgs e)
    {
        DirectoriesList.SelectedItem = null;
        ShowReferencePanel(EquipmentPanel, EquipmentNavButton);
    }

    private void ShowReferencePanel(FrameworkElement panel, Wpf.Ui.Controls.Button? navigationButton)
    {
        MaterialsPanel.Visibility = panel == MaterialsPanel ? Visibility.Visible : Visibility.Collapsed;
        FiguresPanel.Visibility = panel == FiguresPanel ? Visibility.Visible : Visibility.Collapsed;
        EquipmentPanel.Visibility = panel == EquipmentPanel ? Visibility.Visible : Visibility.Collapsed;
        CustomDirectoryPanel.Visibility = panel == CustomDirectoryPanel ? Visibility.Visible : Visibility.Collapsed;

        MaterialsNavButton.Appearance = navigationButton == MaterialsNavButton
            ? Wpf.Ui.Controls.ControlAppearance.Primary
            : Wpf.Ui.Controls.ControlAppearance.Transparent;
        FiguresNavButton.Appearance = navigationButton == FiguresNavButton
            ? Wpf.Ui.Controls.ControlAppearance.Primary
            : Wpf.Ui.Controls.ControlAppearance.Transparent;
        EquipmentNavButton.Appearance = navigationButton == EquipmentNavButton
            ? Wpf.Ui.Controls.ControlAppearance.Primary
            : Wpf.Ui.Controls.ControlAppearance.Transparent;
    }

    private async void AddGroup_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (_api is null || !_isAdministrator) return;
        if (string.IsNullOrWhiteSpace(NewGroupNameBox.Text))
        { SetStatus("Введите название новой группы.", true); NewGroupNameBox.Focus(); return; }
        var created = await _api.AddReferenceDirectoryGroupAsync(NewGroupNameBox.Text);
        NewGroupNameBox.Clear();
        await LoadDirectoriesAsync();
        DirectoryGroupBox.SelectedItem = _directoryGroups.FirstOrDefault(x => x.Id == created.Id);
        CreateDirectoryPanel.Visibility = Visibility.Visible;
        NewDirectoryNameBox.Focus();
        SetStatus("Группа создана. Теперь добавьте в неё справочник.", false);
    });

    private void NewGroupNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        AddGroup_Click(AddGroupButton, new RoutedEventArgs());
    }

    private void DirectoryGroupHeader_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Right || sender is not FrameworkElement header
            || header.DataContext is not CollectionViewGroup viewGroup) return;
        var group = _directoryGroups.FirstOrDefault(x =>
            string.Equals(x.Name, viewGroup.Name?.ToString(), StringComparison.CurrentCulture));
        if (group is null) return; // «Без группы» — служебный раздел.

        var delete = new MenuItem
        {
            Header = "Удалить группу",
            IsEnabled = _isAdministrator
        };
        delete.Click += async (_, _) => await DeleteDirectoryGroupAsync(group);
        var menu = new ContextMenu { PlacementTarget = header };
        menu.Items.Add(delete);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private async Task DeleteDirectoryGroupAsync(ReferenceDirectoryGroupItem group)
    {
        if (_api is null || !_isAdministrator) return;
        var directoryCount = _directories.Count(x => !x.IsPlaceholder && x.GroupId == group.Id);
        var consequence = directoryCount == 0
            ? "Группа пуста и будет удалена без возможности восстановления."
            : $"Группа содержит справочников: {directoryCount}. Они будут перемещены в раздел «Без группы» и не будут удалены.";
        var dialog = new PasswordConfirmationWindow(
            "Удалить группу справочников",
            $"Группа «{group.Name}» будет удалена. {consequence}",
            "Удалить группу")
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true) return;

        await RunAsync(async () =>
        {
            await _api.DeleteReferenceDirectoryGroupAsync(group.Id, dialog.Password);
            await LoadDirectoriesAsync();
            SetStatus($"Группа «{group.Name}» удалена.", false);
        });
    }

    private async void AddDirectory_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (_api is null || !_isAdministrator) return;
        var groupId = DirectoryGroupBox.SelectedItem is ReferenceDirectoryGroupItem group ? group.Id : (Guid?)null;
        var created = await _api.AddReferenceDirectoryAsync(groupId, NewDirectoryNameBox.Text, null);
        NewDirectoryNameBox.Clear();
        CreateDirectoryPanel.Visibility = Visibility.Collapsed;
        await LoadDirectoriesAsync(created.Id);
        SetStatus("Справочник создан.", false);
    });

    private async void DirectoriesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DirectoriesList.SelectedItem is DirectoryRow { IsPlaceholder: true } placeholder)
        {
            DirectoryGroupBox.SelectedItem = _directoryGroups.FirstOrDefault(x => x.Id == placeholder.GroupId);
            CreateDirectoryPanel.Visibility = Visibility.Visible;
            NewDirectoryNameBox.Focus();
            ClearDirectorySelection();
        }
        else if (DirectoriesList.SelectedItem is DirectoryRow row) await RunAsync(() => LoadDirectoryValuesAsync(row));
        else ClearDirectorySelection();
    }

    private async void AddDirectoryValue_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (_api is null || !_isAdministrator || DirectoriesList.SelectedItem is not DirectoryRow directory)
        { SetStatus("Выберите справочник.", true); return; }
        await _api.AddReferenceDirectoryValueAsync(directory.Id, DirectoryValueNameBox.Text);
        DirectoryValueNameBox.Clear();
        await LoadDirectoriesAsync(directory.Id);
        SetStatus("Значение добавлено.", false);
    });

    private async void RenameDirectoryValue_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (_api is null || !_isAdministrator || DirectoriesList.SelectedItem is not DirectoryRow directory || DirectoryValuesGrid.SelectedItem is not DirectoryValueRow value)
        { SetStatus("Выберите значение.", true); return; }
        await _api.UpdateReferenceDirectoryValueAsync(directory.Id, value.Id, DirectoryValueNameBox.Text, value.IsArchived);
        await LoadDirectoryValuesAsync(directory);
        SetStatus("Значение сохранено.", false);
    });

    private async void ArchiveDirectoryValue_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (_api is null || !_isAdministrator || DirectoriesList.SelectedItem is not DirectoryRow directory || DirectoryValuesGrid.SelectedItem is not DirectoryValueRow value)
        { SetStatus("Выберите значение.", true); return; }
        await _api.UpdateReferenceDirectoryValueAsync(directory.Id, value.Id, value.Name, !value.IsArchived);
        await LoadDirectoryValuesAsync(directory);
        SetStatus(value.IsArchived ? "Значение восстановлено." : "Значение архивировано.", false);
    });

    private async void ArchiveDirectory_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (_api is null || !_isAdministrator || DirectoriesList.SelectedItem is not DirectoryRow directory)
        { SetStatus("Выберите справочник.", true); return; }
        await _api.UpdateReferenceDirectoryAsync(directory.Id, directory.GroupId, directory.Name, directory.Description, true);
        await LoadDirectoriesAsync();
        SetStatus("Справочник архивирован.", false);
    });

    private void DirectoryValuesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DirectoryValuesGrid.SelectedItem is DirectoryValueRow value)
        {
            DirectoryValueNameBox.Text = value.Name;
            ArchiveDirectoryValueButton.Content = value.IsArchived ? "Восстановить" : "В архив";
        }
    }

    private void DirectoryValueSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyDirectoryValueFilter();

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

    private async Task ImportSystemReferencesAsync(CatalogReferenceType type)
    {
        if (_api is null || !_isAdministrator) return;
        var path = SelectImportFile();
        if (path is null) return;

        await RunAsync(async () =>
        {
            var names = await CsvReferenceImportReader.ReadNamesAsync(path);
            if (names.Count == 0)
            {
                SetStatus("CSV-файл не содержит значений для импорта.", true);
                return;
            }

            var result = await _api.ImportCatalogReferencesAsync(type, names);
            await ReloadReferencesAsync();
            SetImportStatus(result);
        });
    }

    private async Task ImportDirectoryValuesAsync()
    {
        if (_api is null || !_isAdministrator) return;
        if (DirectoriesList.SelectedItem is not DirectoryRow directory)
        {
            SetStatus("Выберите пользовательский справочник.", true);
            return;
        }

        var path = SelectImportFile();
        if (path is null) return;
        await RunAsync(async () =>
        {
            var names = await CsvReferenceImportReader.ReadNamesAsync(path);
            if (names.Count == 0)
            {
                SetStatus("CSV-файл не содержит значений для импорта.", true);
                return;
            }

            var result = await _api.ImportReferenceDirectoryValuesAsync(directory.Id, names);
            await LoadDirectoriesAsync(directory.Id);
            SetImportStatus(result);
        });
    }

    private string? SelectImportFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Импорт значений справочника",
            Filter = "CSV (*.csv)|*.csv|Все файлы (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FileName : null;
    }

    private void SetImportStatus(ReferenceImportResult result) => SetStatus(
        $"Импорт завершён: добавлено — {result.Added}, пропущено — {result.Skipped}.", false);

    private async Task ReloadReferencesAsync()
    {
        var references = await _api!.GetCatalogReferencesAsync();
        Replace(_materials, references.Materials);
        Replace(_figures, references.Figures);
        Replace(_equipment, references.Equipment);
        UpdateReferenceCounts();
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
            AddMaterialButton, RenameMaterialButton, ImportMaterialsButton, AddFigureButton, ImportFiguresButton,
            RenameFigureButton, DeleteMaterialButton, DeleteFigureButton, AddEquipmentButton, RenameEquipmentButton, DeleteEquipmentButton
            , ImportEquipmentButton, ImportDirectoryValuesButton
            , ReloadDirectoriesButton, NewDirectoryButton, AddGroupButton, AddDirectoryButton, AddDirectoryValueButton,
            RenameDirectoryValueButton, ArchiveDirectoryValueButton, ArchiveDirectoryButton,
            NewGroupNameBox, NewDirectoryNameBox, DirectoryGroupBox, DirectoryValueNameBox
        }) control.IsEnabled = enabled;
        foreach (var grid in new[] { MaterialsGrid, FiguresGrid, EquipmentGrid, DirectoryValuesGrid })
            foreach (var item in grid.ContextMenu!.Items.OfType<MenuItem>())
                if (item.Tag is true) item.IsEnabled = enabled;
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

    private void UpdateReferenceCounts()
    {
        MaterialsCountText.Text = _materials.Count.ToString(CultureInfo.InvariantCulture);
        FiguresCountText.Text = _figures.Count.ToString(CultureInfo.InvariantCulture);
        EquipmentCountText.Text = _equipment.Count.ToString(CultureInfo.InvariantCulture);
    }

    private static void SelectName(DataGrid grid, TextBox box)
    {
        if (grid.SelectedItem is CatalogReferenceItem item) box.Text = item.Name;
    }

    private void OpenSystemReferenceArticle(DataGrid grid, string category)
    {
        if (grid.SelectedItem is not CatalogReferenceItem item)
        {
            SetStatus("Выберите значение, карточку которого нужно открыть.", true);
            return;
        }

        ShowReferenceArticle(new PositionSelection(
            item.Id, item.Name, item.Type, null, category, item.ArticleRtf, grid));
    }

    private void OpenDirectoryValueArticle()
    {
        if (DirectoriesList.SelectedItem is not DirectoryRow directory
            || DirectoryValuesGrid.SelectedItem is not DirectoryValueRow value)
        {
            SetStatus("Выберите значение, карточку которого нужно открыть.", true);
            return;
        }

        ShowReferenceArticle(new PositionSelection(
            value.Id, value.Name, null, directory.Id, directory.Name, value.ArticleRtf, DirectoryValuesGrid));
    }

    private void ConfigurePositionMenu(DataGrid grid)
    {
        grid.PreviewMouseRightButtonDown += PositionGrid_PreviewMouseRightButtonDown;
        grid.ContextMenuOpening += (_, args) =>
        {
            if (grid.SelectedItem is null) args.Handled = true;
        };
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("Редактировать", async (_, _) => await EditSelectedPositionAsync(grid), true));
        menu.Items.Add(CreateMenuItem("Просмотреть", (_, _) => ViewSelectedPosition(grid), false));
        menu.Items.Add(CreateMenuItem("Удалить", async (_, _) => await DeleteSelectedPositionAsync(grid), true));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Дублировать в текущий раздел", async (_, _) => await DuplicateSelectedPositionAsync(grid), true));
        menu.Items.Add(CreateMenuItem("Копировать в…", async (_, _) => await TransferSelectedPositionAsync(grid, false), true));
        menu.Items.Add(CreateMenuItem("Перенести в…", async (_, _) => await TransferSelectedPositionAsync(grid, true), true));
        grid.ContextMenu = menu;
    }

    private MenuItem CreateMenuItem(string header, RoutedEventHandler handler, bool requiresAdministrator)
    {
        var item = new MenuItem
        {
            Header = header,
            Tag = requiresAdministrator,
            IsEnabled = !requiresAdministrator || _isAdministrator
        };
        item.Click += handler;
        return item;
    }

    private void PositionGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is null)
        {
            grid.SelectedItem = null;
            return;
        }
        grid.SelectedItem = row.Item;
        row.Focus();
    }

    private static T? FindVisualParent<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private PositionSelection? GetSelectedPosition(DataGrid grid)
    {
        if (grid == DirectoryValuesGrid)
        {
            return DirectoriesList.SelectedItem is DirectoryRow directory
                   && grid.SelectedItem is DirectoryValueRow value
                ? new PositionSelection(value.Id, value.Name, null, directory.Id, directory.Name, value.ArticleRtf, grid)
                : null;
        }

        if (grid.SelectedItem is not CatalogReferenceItem item) return null;
        var type = grid == MaterialsGrid ? CatalogReferenceType.Material
            : grid == FiguresGrid ? CatalogReferenceType.Figure
            : CatalogReferenceType.Equipment;
        return new PositionSelection(item.Id, item.Name, type, null, SystemReferenceName(type), item.ArticleRtf, grid);
    }

    private async Task EditSelectedPositionAsync(DataGrid grid)
    {
        var source = GetSelectedPosition(grid);
        if (source is null || _api is null || !_isAdministrator) return;
        var dialog = new ReferencePositionActionWindow("Редактировать позицию", source.Name)
            { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        await RunAsync(async () =>
        {
            if (source.SystemType.HasValue)
                await _api.RenameCatalogReferenceAsync(source.SystemType.Value, source.Id, dialog.PositionName);
            else
                await _api.UpdateReferenceDirectoryValueAsync(
                    source.DirectoryId!.Value, source.Id, dialog.PositionName,
                    ((DirectoryValueRow)grid.SelectedItem).IsArchived);
            await RefreshPositionsAsync(source.DirectoryId);
            SetStatus("Позиция сохранена.", false);
        });
    }

    private void ViewSelectedPosition(DataGrid grid)
    {
        var source = GetSelectedPosition(grid);
        if (source is null) return;
        ShowReferenceArticle(source);
    }

    private void ShowReferenceArticle(PositionSelection source)
    {
        Func<string?, Task>? save = null;
        if (_api is not null && _isAdministrator)
        {
            save = source.SystemType.HasValue
                ? article => _api.UpdateCatalogReferenceArticleAsync(source.SystemType.Value, source.Id, article)
                : article => _api.UpdateReferenceDirectoryValueArticleAsync(
                    source.DirectoryId!.Value, source.Id, article);
        }
        new ReferenceArticleWindow(
            source.SectionName, source.Name, source.ArticleRtf, _isAdministrator, save)
            { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private async Task DeleteSelectedPositionAsync(DataGrid grid)
    {
        var source = GetSelectedPosition(grid);
        if (source is null || _api is null || !_isAdministrator) return;
        if (source.SystemType.HasValue)
        {
            await DeleteAsync(source.SystemType.Value, grid);
            return;
        }

        var confirmation = MessageBox.Show(Window.GetWindow(this),
            $"Удалить позицию «{source.Name}» без возможности восстановления?",
            "Удаление позиции", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;
        await RunAsync(async () =>
        {
            await _api.DeleteReferenceDirectoryValueAsync(source.DirectoryId!.Value, source.Id);
            await LoadDirectoriesAsync(source.DirectoryId);
            SetStatus("Позиция удалена.", false);
        });
    }

    private async Task DuplicateSelectedPositionAsync(DataGrid grid)
    {
        var source = GetSelectedPosition(grid);
        if (source is null || _api is null || !_isAdministrator) return;
        var dialog = new ReferencePositionActionWindow("Дублировать позицию", source.Name)
            { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        var destination = new ReferencePositionDestination(source.SectionName, source.SystemType, source.DirectoryId);
        await RunAsync(async () =>
        {
            await AddPositionAsync(destination, dialog.PositionName);
            await RefreshPositionsAsync(source.DirectoryId);
            SetStatus("Копия позиции создана в текущем разделе.", false);
        });
    }

    private async Task TransferSelectedPositionAsync(DataGrid grid, bool move)
    {
        var source = GetSelectedPosition(grid);
        if (source is null || _api is null || !_isAdministrator) return;
        var destinations = BuildDestinations(source);
        if (destinations.Count == 0)
        {
            SetStatus("Нет другого раздела для этой операции.", true);
            return;
        }

        var dialog = new ReferencePositionActionWindow(
            move ? "Перенести позицию" : "Копировать позицию", source.Name, destinations)
            { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true || dialog.Destination is null) return;

        string? password = null;
        if (move && (source.SystemType.HasValue || dialog.Destination.SystemType.HasValue))
        {
            var confirmation = new PasswordConfirmationWindow(
                "Перенести позицию",
                $"Подтвердите перенос через системный справочник. После создания позиции в разделе «{dialog.Destination.DisplayName}» исходная позиция «{source.Name}» будет удалена.",
                "Перенести") { Owner = Window.GetWindow(this) };
            if (confirmation.ShowDialog() != true) return;
            password = confirmation.Password;
        }

        await RunAsync(async () =>
        {
            var createdId = await AddPositionAsync(dialog.Destination, dialog.PositionName);
            if (move)
            {
                try
                {
                    await DeletePositionAsync(source, password);
                }
                catch
                {
                    await TryRollbackCreatedPositionAsync(dialog.Destination, createdId, password);
                    throw;
                }
            }
            await RefreshPositionsAsync(dialog.Destination.DirectoryId ?? source.DirectoryId);
            SetStatus(move ? "Позиция перенесена." : "Позиция скопирована.", false);
        });
    }

    private List<ReferencePositionDestination> BuildDestinations(PositionSelection source)
    {
        var result = new List<ReferencePositionDestination>
        {
            new("Системные / Материалы", CatalogReferenceType.Material, null),
            new("Системные / Фигуры", CatalogReferenceType.Figure, null),
            new("Системные / Оборудование", CatalogReferenceType.Equipment, null)
        };
        result.AddRange(_directories.Where(directory => !directory.IsPlaceholder).Select(directory => new ReferencePositionDestination(
            $"{directory.GroupName} / {directory.Name}", null, directory.Id)));
        return result.Where(destination => destination.SystemType != source.SystemType
                                           || destination.DirectoryId != source.DirectoryId).ToList();
    }

    private async Task<Guid> AddPositionAsync(ReferencePositionDestination destination, string name)
    {
        if (destination.SystemType.HasValue)
            return (await _api!.AddCatalogReferenceAsync(destination.SystemType.Value, name)).Id;
        return (await _api!.AddReferenceDirectoryValueAsync(destination.DirectoryId!.Value, name)).Id;
    }

    private async Task DeletePositionAsync(PositionSelection source, string? password)
    {
        if (source.SystemType.HasValue)
            await _api!.DeleteCatalogReferenceAsync(source.SystemType.Value, source.Id, password ?? string.Empty);
        else
            await _api!.DeleteReferenceDirectoryValueAsync(source.DirectoryId!.Value, source.Id);
    }

    private async Task TryRollbackCreatedPositionAsync(
        ReferencePositionDestination destination, Guid createdId, string? password)
    {
        try
        {
            if (destination.SystemType.HasValue && !string.IsNullOrEmpty(password))
                await _api!.DeleteCatalogReferenceAsync(destination.SystemType.Value, createdId, password);
            else if (destination.DirectoryId.HasValue)
                await _api!.DeleteReferenceDirectoryValueAsync(destination.DirectoryId.Value, createdId);
        }
        catch { /* The original error remains the useful one for the user. */ }
    }

    private async Task RefreshPositionsAsync(Guid? selectedDirectoryId)
    {
        await ReloadReferencesAsync();
        await LoadDirectoriesAsync(selectedDirectoryId);
    }

    private static string SystemReferenceName(CatalogReferenceType type) => type switch
    {
        CatalogReferenceType.Material => "Материалы",
        CatalogReferenceType.Figure => "Фигуры",
        CatalogReferenceType.Equipment => "Оборудование",
        _ => type.ToString()
    };

    private void MaterialsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => SelectName(MaterialsGrid, MaterialNameBox);
    private void FiguresGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => SelectName(FiguresGrid, FigureNameBox);
    private void EquipmentGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => SelectName(EquipmentGrid, EquipmentNameBox);
    private void OpenMaterialArticle_Click(object sender, RoutedEventArgs e) => OpenSystemReferenceArticle(MaterialsGrid, "Материалы");
    private void OpenFigureArticle_Click(object sender, RoutedEventArgs e) => OpenSystemReferenceArticle(FiguresGrid, "Фигуры");
    private void OpenEquipmentArticle_Click(object sender, RoutedEventArgs e) => OpenSystemReferenceArticle(EquipmentGrid, "Оборудование");
    private void OpenDirectoryValueArticle_Click(object sender, RoutedEventArgs e) => OpenDirectoryValueArticle();
    private void MaterialsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSystemReferenceArticle(MaterialsGrid, "Материалы");
    private void FiguresGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSystemReferenceArticle(FiguresGrid, "Фигуры");
    private void EquipmentGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSystemReferenceArticle(EquipmentGrid, "Оборудование");
    private void DirectoryValuesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenDirectoryValueArticle();
    private void BackToCatalog_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
    private async void DeleteMaterial_Click(object sender, RoutedEventArgs e) => await DeleteAsync(CatalogReferenceType.Material, MaterialsGrid);
    private async void DeleteFigure_Click(object sender, RoutedEventArgs e) => await DeleteAsync(CatalogReferenceType.Figure, FiguresGrid);
    private async void DeleteEquipment_Click(object sender, RoutedEventArgs e) => await DeleteAsync(CatalogReferenceType.Equipment, EquipmentGrid);    private async void AddMaterial_Click(object sender, RoutedEventArgs e) => await AddAsync(CatalogReferenceType.Material, MaterialNameBox);
    private async void ImportMaterials_Click(object sender, RoutedEventArgs e) => await ImportSystemReferencesAsync(CatalogReferenceType.Material);
    private async void ImportFigures_Click(object sender, RoutedEventArgs e) => await ImportSystemReferencesAsync(CatalogReferenceType.Figure);
    private async void ImportEquipment_Click(object sender, RoutedEventArgs e) => await ImportSystemReferencesAsync(CatalogReferenceType.Equipment);
    private async void ImportDirectoryValues_Click(object sender, RoutedEventArgs e) => await ImportDirectoryValuesAsync();
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
            Action = EventName(entry);
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

        private static string EventName(AuditLogEntry entry) => entry.AccessType switch
        {
            EmployeeAccessEventType.LoggedIn => "Вход в систему",
            EmployeeAccessEventType.LoggedOut => "Выход из системы",
            _ => EventName(entry.Type)
        };

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

    private sealed class DirectoryRow
    {
        public DirectoryRow(ReferenceDirectoryItem item, string groupName)
        {
            Id = item.Id; GroupId = item.GroupId; Name = item.Name;
            Description = item.Description?.Trim() ?? string.Empty;
            ValueCount = item.ValueCount; GroupName = groupName;
            Subtitle = $"{groupName} · {item.ValueCount}";
        }

        public DirectoryRow(ReferenceDirectoryGroupItem group)
        {
            GroupId = group.Id; Name = "Добавить справочник"; Description = "В этой группе пока нет справочников";
            GroupName = group.Name; Subtitle = Description; IsPlaceholder = true;
        }

        public Guid Id { get; }
        public Guid? GroupId { get; }
        public string Name { get; }
        public string Description { get; }
        public int ValueCount { get; }
        public string GroupName { get; }
        public string Subtitle { get; }
        public bool IsPlaceholder { get; }
    }

    private sealed class DirectoryValueRow(ReferenceDirectoryValueItem item)
    {
        public Guid Id { get; } = item.Id;
        public string Name { get; } = item.Name;
        public bool IsArchived { get; } = item.IsArchived;
        public string State { get; } = item.IsArchived ? "В архиве" : "Активно";
        public string Updated { get; } = item.UpdatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        public string? ArticleRtf { get; } = item.ArticleRtf;
    }

    private sealed record PositionSelection(
        Guid Id,
        string Name,
        CatalogReferenceType? SystemType,
        Guid? DirectoryId,
        string SectionName,
        string? ArticleRtf,
        DataGrid Grid);
}
