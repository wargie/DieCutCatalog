using System.Globalization;
using System.Windows;
using DieCutCatalog.Application.Catalog;

namespace DieCutCatalog.Desktop.Views;

public partial class JustCutPriceWindow : Window
{
    public JustCutPriceParameters? Parameters { get; private set; }

    public JustCutPriceWindow(DieCutDetails dieCut)
    {
        InitializeComponent();
        KnifeSummaryText.Text =
            $"Нож {dieCut.Number}: {dieCut.Figure}, {dieCut.X:0.###} × {dieCut.Y:0.###} мм, " +
            $"вал Z{dieCut.Shaft}, ручьёв {dieCut.Streams}, повторов {dieCut.Repeats}.";
    }

    private void Calculate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var knifeHeight = ParseDecimal(KnifeHeightBox.Text, "Высота ножа");
            var substrateThickness = ParseDecimal(SubstrateThicknessBox.Text, "Толщина подложки");
            if (!int.TryParse(AngleSharpeningBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var angle))
                throw new FormatException("Проверьте угол заточки.");

            Parameters = new JustCutPriceParameters(
                RushOrderBox.IsChecked == true,
                knifeHeight,
                substrateThickness,
                angle,
                EdgeUnder2MmBox.IsChecked == true,
                AntiAdhesionCoatingBox.IsChecked == true,
                LaserHardeningBox.IsChecked == true,
                HardeningCoatingBox.IsChecked == true);
            DialogResult = true;
        }
        catch (FormatException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private static decimal ParseDecimal(string text, string field)
    {
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value)) return value;
        if (decimal.TryParse(text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out value)) return value;
        throw new FormatException($"Проверьте поле «{field}».");
    }
}
