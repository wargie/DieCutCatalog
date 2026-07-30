using DieCutCatalog.Mobile.Models;
using DieCutCatalog.Mobile.Services;

namespace DieCutCatalog.Mobile.Views;

[QueryProperty(nameof(KnifeId), "id")]
public partial class KnifeDetailPage : ContentPage
{
    private Guid id;
    private KnifeListItem? knife;
    public string KnifeId { set => Guid.TryParse(value, out id); }

    public KnifeDetailPage() => InitializeComponent();

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (id == Guid.Empty) return;
        try
        {
            knife = await CatalogApiClient.Current.GetKnifeAsync(id);
            BindingContext = knife;
        }
        catch (ApiException exception)
        {
            await DisplayAlert("Ошибка", exception.Message, "OK");
        }
    }

    private async void AddCirculation_Clicked(object sender, EventArgs e)
    {
        if (knife is null) return;
        await Shell.Current.GoToAsync($"{nameof(AddCirculationPage)}?id={knife.Id}");
    }
}
