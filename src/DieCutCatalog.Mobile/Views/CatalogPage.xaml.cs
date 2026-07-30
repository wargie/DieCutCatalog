using System.Collections.ObjectModel;
using DieCutCatalog.Mobile.Data;
using DieCutCatalog.Mobile.Models;

namespace DieCutCatalog.Mobile.Views;

public partial class CatalogPage : ContentPage
{
    private readonly ObservableCollection<KnifeListItem> visible = [];
    private string equipment = string.Empty;

    public CatalogPage()
    {
        InitializeComponent();
        KnivesList.ItemsSource = visible;
        KnivesList.ItemTemplate = (DataTemplate)Resources["CompactKnifeTemplate"];
        ApplyFilter();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        AvatarInitials.Text = SessionProfile.Initials;
    }

    private void ApplyFilter()
    {
        var search = SearchBox.Text?.Trim() ?? string.Empty;
        var filtered = DemoCatalog.Knives.Where(x =>
            (equipment.Length == 0 || x.Equipment == equipment) &&
            (search.Length == 0 || x.Number.Contains(search, StringComparison.OrdinalIgnoreCase)));
        visible.Clear();
        foreach (var knife in filtered) visible.Add(knife);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void Equipment_Clicked(object sender, EventArgs e)
    {
        equipment = (sender as Button)?.CommandParameter?.ToString() ?? string.Empty;
        ApplyFilter();
    }

    private void Compact_Clicked(object sender, EventArgs e)
    {
        KnivesList.ItemTemplate = (DataTemplate)Resources["CompactKnifeTemplate"];
        CompactButton.BackgroundColor = Color.FromArgb("#2F5F92");
        CompactButton.TextColor = Colors.White;
        DetailedButton.BackgroundColor = Color.FromArgb("#F7F8FA");
        DetailedButton.TextColor = Color.FromArgb("#1F1F1F");
    }

    private void Detailed_Clicked(object sender, EventArgs e)
    {
        KnivesList.ItemTemplate = (DataTemplate)Resources["DetailedKnifeTemplate"];
        DetailedButton.BackgroundColor = Color.FromArgb("#2F5F92");
        DetailedButton.TextColor = Colors.White;
        CompactButton.BackgroundColor = Color.FromArgb("#F7F8FA");
        CompactButton.TextColor = Color.FromArgb("#1F1F1F");
    }

    private async void KnivesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not KnifeListItem knife) return;
        KnivesList.SelectedItem = null;
        await Shell.Current.GoToAsync($"{nameof(KnifeDetailPage)}?number={Uri.EscapeDataString(knife.Number)}");
    }

    private async void Profile_Tapped(object sender, TappedEventArgs e) =>
        await Shell.Current.GoToAsync(nameof(ProfilePage));
}
