using System.Globalization;
using DieCutCatalog.Mobile.Models;
using DieCutCatalog.Mobile.Services;

namespace DieCutCatalog.Mobile.Views;

[QueryProperty(nameof(KnifeId), "id")]
public partial class AddCirculationPage : ContentPage
{
    private Guid id;
    private KnifeListItem? knife;
    private bool quantityMode = true;
    public string KnifeId { set => Guid.TryParse(value, out id); }

    public AddCirculationPage() => InitializeComponent();

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (knife is not null || id == Guid.Empty) return;
        try
        {
            knife = await CatalogApiClient.Current.GetKnifeAsync(id);
            UpdatePreview();
        }
        catch (ApiException exception)
        {
            await DisplayAlert("Ошибка", exception.Message, "OK");
        }
    }

    private void QuantityMode_Clicked(object sender, EventArgs e) => SetMode(true);
    private void RunLengthMode_Clicked(object sender, EventArgs e) => SetMode(false);

    private void SetMode(bool useQuantity)
    {
        quantityMode = useQuantity;
        QuantityModeButton.BackgroundColor = Color.FromArgb(useQuantity ? "#2F5F92" : "#F7F8FA");
        QuantityModeButton.TextColor = useQuantity ? Colors.White : Color.FromArgb("#1F1F1F");
        RunLengthModeButton.BackgroundColor = Color.FromArgb(useQuantity ? "#F7F8FA" : "#2F5F92");
        RunLengthModeButton.TextColor = useQuantity ? Color.FromArgb("#1F1F1F") : Colors.White;
        InputLabel.Text = useQuantity ? "Тираж, шт." : "Пробег, м";
        UsageEntry.Placeholder = useQuantity ? "Например, 50000" : "Например, 3825,5";
        UsageEntry.Text = string.Empty;
        UpdatePreview();
        UsageEntry.Focus();
    }

    private void Usage_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (knife is null)
        {
            KnifeTitle.Text = "Загрузка...";
            return;
        }

        KnifeTitle.Text = $"Нож {knife.Number} · {knife.Equipment}";
        if (!TryCalculate(out var quantity, out var runLength, out var revolutions))
        {
            QuantityText.Text = "0 шт.";
            RunLengthText.Text = "0 м";
            RevolutionsText.Text = "0";
            return;
        }

        QuantityText.Text = $"{quantity.ToString("N0", CultureInfo.CurrentCulture)} шт.";
        RunLengthText.Text = $"{runLength.ToString("N2", CultureInfo.CurrentCulture)} м";
        RevolutionsText.Text = revolutions.ToString("N0", CultureInfo.CurrentCulture);
    }

    private bool TryCalculate(out long quantity, out decimal runLength, out long revolutions)
    {
        quantity = 0;
        runLength = 0;
        revolutions = 0;
        if (knife is null || knife.Streams <= 0 || knife.Shaft <= 0) return false;

        var labelPitchMeters = knife.Length / 1000m + knife.GapYMillimeters;
        if (labelPitchMeters <= 0) return false;

        if (quantityMode)
        {
            if (!long.TryParse(UsageEntry?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out quantity) || quantity <= 0)
                return false;
            runLength = quantity / (decimal)knife.Streams * labelPitchMeters;
        }
        else
        {
            if (!TryParsePositiveDecimal(UsageEntry?.Text, out runLength)) return false;
            quantity = checked((long)decimal.Round(
                runLength * knife.Streams / labelPitchMeters,
                0,
                MidpointRounding.AwayFromZero));
            if (quantity <= 0) return false;
        }

        var rapportMeters = knife.Shaft * 3.175m / 1000m;
        revolutions = checked((long)decimal.Ceiling(runLength / rapportMeters));
        return true;
    }

    private static bool TryParsePositiveDecimal(string? text, out decimal value)
    {
        var normalized = text?.Trim().Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out value) && value > 0;
    }

    private async void Cancel_Clicked(object sender, EventArgs e) => await Shell.Current.GoToAsync("..");

    private async void Confirm_Clicked(object sender, EventArgs e)
    {
        if (knife is null || !TryCalculate(out var quantity, out var runLength, out _))
        {
            await DisplayAlert("Пробег", quantityMode
                ? "Введите положительное целое количество этикеток."
                : "Введите положительный пробег в метрах.", "OK");
            return;
        }

        ConfirmButton.IsEnabled = false;
        try
        {
            await CatalogApiClient.Current.AddCirculationAsync(
                knife.Id,
                quantityMode ? quantity : null,
                quantityMode ? null : runLength);
            await DisplayAlert("Пробег", quantityMode
                ? "Тираж добавлен и пересчитан сервером."
                : "Пробег добавлен и пересчитан сервером.", "OK");
            await Shell.Current.GoToAsync("..");
        }
        catch (ApiException exception)
        {
            await DisplayAlert("Ошибка", exception.Message, "OK");
        }
        finally
        {
            ConfirmButton.IsEnabled = true;
        }
    }
}
