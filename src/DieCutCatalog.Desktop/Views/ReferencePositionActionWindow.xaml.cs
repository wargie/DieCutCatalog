using System.Windows;
using DieCutCatalog.Application.Catalog;

namespace DieCutCatalog.Desktop.Views;

public sealed record ReferencePositionDestination(
    string DisplayName,
    CatalogReferenceType? SystemType,
    Guid? DirectoryId);

public partial class ReferencePositionActionWindow : Window
{
    public ReferencePositionActionWindow(
        string actionTitle,
        string sourceName,
        IReadOnlyList<ReferencePositionDestination>? destinations = null)
    {
        InitializeComponent();
        Title = actionTitle;
        HeadingText.Text = actionTitle;
        var isEdit = actionTitle.StartsWith("Редактировать", StringComparison.Ordinal);
        var isDuplicate = actionTitle.StartsWith("Дублировать", StringComparison.Ordinal);
        HintText.Text = destinations is null && isEdit
            ? "Измените название выбранной позиции."
            : destinations is null
            ? "Укажите название создаваемой копии."
            : "Выберите раздел назначения и при необходимости измените название.";
        NameBox.Text = isDuplicate ? $"{sourceName} — копия" : sourceName;
        NameBox.SelectAll();
        ConfirmButton.Content = isEdit ? "Сохранить"
            : actionTitle.StartsWith("Перенести", StringComparison.Ordinal) ? "Перенести"
            : actionTitle.StartsWith("Копировать", StringComparison.Ordinal) ? "Копировать"
            : "Создать";

        if (destinations is null)
        {
            DestinationPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            DestinationBox.ItemsSource = destinations;
            DestinationBox.SelectedIndex = destinations.Count > 0 ? 0 : -1;
        }

        Loaded += (_, _) => NameBox.Focus();
    }

    public string PositionName => NameBox.Text.Trim();
    internal ReferencePositionDestination? Destination =>
        DestinationBox.SelectedItem as ReferencePositionDestination;

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (PositionName.Length is < 1 or > 200)
        {
            ShowValidation("Название должно содержать от 1 до 200 символов.");
            return;
        }
        if (DestinationPanel.Visibility == Visibility.Visible && Destination is null)
        {
            ShowValidation("Выберите раздел назначения.");
            return;
        }
        DialogResult = true;
    }

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
