using System.IO;
using System.Windows;
using DieCutCatalog.Application.Catalog;

namespace DieCutCatalog.Desktop.Views;

public partial class ExcelImportPreviewWindow : Window
{
    public bool OverwriteExisting => OverwriteBox.IsChecked == true;

    public ExcelImportPreviewWindow(string filePath, ExcelImportPreview preview)
    {
        InitializeComponent();
        FileNameText.Text = Path.GetFileName(filePath);
        TotalText.Text = preview.TotalRows.ToString();
        ValidText.Text = preview.ValidRows.ToString();
        NewText.Text = preview.NewRows.ToString();
        ExistingText.Text = preview.ExistingRows.ToString();
        ErrorsText.Text = preview.ErrorRows.ToString();
        IssuesGrid.ItemsSource = preview.Issues;
        OverwriteBox.IsEnabled = preview.ExistingRows > 0;
        ImportButton.IsEnabled = preview.ValidRows > 0;
    }

    private void Import_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
