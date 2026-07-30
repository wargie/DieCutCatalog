using System.Globalization;
using DieCutCatalog.Mobile.Data;
using DieCutCatalog.Mobile.Models;

namespace DieCutCatalog.Mobile.Views;

[QueryProperty(nameof(Number), "number")]
public partial class AddCirculationPage : ContentPage
{
    private KnifeListItem knife = DemoCatalog.Knives[0];
    public string Number { set { knife = DemoCatalog.Find(Uri.UnescapeDataString(value)); Update(); } }

    public AddCirculationPage()
    {
        InitializeComponent();
        Update();
    }

    private void Quantity_TextChanged(object sender, TextChangedEventArgs e) => Update();

    private void Update()
    {
        KnifeTitle.Text = $"Нож {knife.Number} · {knife.Equipment}";
        if (!long.TryParse(QuantityEntry?.Text, out var quantity) || quantity <= 0)
        {
            RunLengthText.Text = "0 м";
            RevolutionsText.Text = "0";
            return;
        }
        var runLength = quantity / (decimal)knife.Streams * (knife.Length + knife.GapYMillimeters) / 1000m;
        var rapport = knife.Shaft * 3.175m / 1000m;
        var revolutions = (long)Math.Ceiling(runLength / rapport);
        RunLengthText.Text = $"{runLength.ToString("N1", CultureInfo.CurrentCulture)} м";
        RevolutionsText.Text = revolutions.ToString("N0", CultureInfo.CurrentCulture);
    }

    private async void Cancel_Clicked(object sender, EventArgs e) => await Shell.Current.GoToAsync("..");

    private async void Confirm_Clicked(object sender, EventArgs e) =>
        await DisplayAlert("Демонстрационный режим", "Подключение операции к серверу будет добавлено следующим блоком.", "OK");
}