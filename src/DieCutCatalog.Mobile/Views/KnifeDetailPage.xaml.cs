using DieCutCatalog.Mobile.Data;
using DieCutCatalog.Mobile.Models;

namespace DieCutCatalog.Mobile.Views;

[QueryProperty(nameof(Number), "number")]
public partial class KnifeDetailPage : ContentPage
{
    private KnifeListItem knife = DemoCatalog.Knives[0];
    public string Number { set { knife = DemoCatalog.Find(Uri.UnescapeDataString(value)); BindingContext = knife; } }

    public KnifeDetailPage()
    {
        InitializeComponent();
        BindingContext = knife;
    }

    private async void AddCirculation_Clicked(object sender, EventArgs e) =>
        await Shell.Current.GoToAsync($"{nameof(AddCirculationPage)}?number={Uri.EscapeDataString(knife.Number)}");
}