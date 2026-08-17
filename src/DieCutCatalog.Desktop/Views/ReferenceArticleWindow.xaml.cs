using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace DieCutCatalog.Desktop.Views;

public partial class ReferenceArticleWindow : Window
{
    private readonly Func<string?, Task>? _saveAsync;
    private string? _savedRtf;

    public ReferenceArticleWindow(
        string category, string title, string? articleRtf = null,
        bool canEdit = false, Func<string?, Task>? saveAsync = null)
    {
        InitializeComponent();
        CategoryText.Text = category;
        ArticleTitle.Text = title;
        _savedRtf = articleRtf;
        _saveAsync = saveAsync;
        EditLink.Visibility = canEdit && saveAsync is not null ? Visibility.Visible : Visibility.Collapsed;
        LoadDocument(articleRtf);
    }

    private void LoadDocument(string? rtf)
    {
        ArticleEditor.Document.Blocks.Clear();
        if (string.IsNullOrWhiteSpace(rtf))
        {
            ArticleEditor.Document.Blocks.Add(new Paragraph(new Run("Описание пока не добавлено."))
                { Foreground = Brushes.Gray });
            return;
        }
        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rtf));
            new TextRange(ArticleEditor.Document.ContentStart, ArticleEditor.Document.ContentEnd)
                .Load(stream, DataFormats.Rtf);
        }
        catch
        {
            ArticleEditor.Document.Blocks.Add(new Paragraph(new Run(rtf)));
        }
    }

    private string? SaveDocument()
    {
        var range = new TextRange(ArticleEditor.Document.ContentStart, ArticleEditor.Document.ContentEnd);
        if (string.IsNullOrWhiteSpace(range.Text)) return null;
        using var stream = new MemoryStream();
        range.Save(stream, DataFormats.Rtf);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (_saveAsync is null) return;
        if (string.IsNullOrWhiteSpace(_savedRtf)) ArticleEditor.Document.Blocks.Clear();
        ArticleEditor.IsReadOnly = false;
        EditorToolbar.Visibility = Visibility.Visible;
        SaveButton.Visibility = Visibility.Visible;
        CancelEditButton.Visibility = Visibility.Visible;
        EditLink.Visibility = Visibility.Collapsed;
        ArticleEditor.Focus();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_saveAsync is null) return;
        try
        {
            SaveButton.IsEnabled = false;
            StatusText.Text = "Сохранение…";
            var rtf = SaveDocument();
            await _saveAsync(rtf);
            _savedRtf = rtf;
            SetViewMode();
            StatusText.Foreground = (Brush)FindResource("SuccessTextBrush");
            StatusText.Text = "Карточка сохранена.";
        }
        catch (Exception exception)
        {
            StatusText.Foreground = (Brush)FindResource("ErrorTextBrush");
            StatusText.Text = exception.Message;
        }
        finally { SaveButton.IsEnabled = true; }
    }

    private void CancelEdit_Click(object sender, RoutedEventArgs e)
    {
        LoadDocument(_savedRtf);
        SetViewMode();
        StatusText.Text = string.Empty;
    }

    private void SetViewMode()
    {
        ArticleEditor.IsReadOnly = true;
        EditorToolbar.Visibility = Visibility.Collapsed;
        SaveButton.Visibility = Visibility.Collapsed;
        CancelEditButton.Visibility = Visibility.Collapsed;
        EditLink.Visibility = Visibility.Visible;
    }

    private void ParagraphStyleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || ArticleEditor.IsReadOnly || ParagraphStyleBox.SelectedItem is not ComboBoxItem item
            || !double.TryParse(item.Tag?.ToString(), out var size)) return;
        ArticleEditor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
        ArticleEditor.Focus();
    }

    private void Strike_Click(object sender, RoutedEventArgs e)
    {
        var current = ArticleEditor.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
        var isStruck = current is TextDecorationCollection decorations
                       && decorations.Any(x => x.Location == TextDecorationLocation.Strikethrough);
        ArticleEditor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty,
            isStruck ? null : TextDecorations.Strikethrough);
        ArticleEditor.Focus();
    }

    private void TextColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string color }) return;
        ArticleEditor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty,
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)));
        ArticleEditor.Focus();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
