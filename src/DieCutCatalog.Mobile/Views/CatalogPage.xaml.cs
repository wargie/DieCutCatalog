using System.Collections.ObjectModel;
using DieCutCatalog.Mobile.Models;
using DieCutCatalog.Mobile.Services;

namespace DieCutCatalog.Mobile.Views;

public partial class CatalogPage : ContentPage
{
    private readonly ObservableCollection<KnifeListItem> all = [];
    private readonly ObservableCollection<KnifeListItem> visible = [];
    private string equipment = string.Empty;
    private bool loaded;

    public CatalogPage()
    {
        InitializeComponent();
        KnivesList.ItemsSource = visible;
        KnivesList.ItemTemplate = (DataTemplate)Resources["CompactKnifeTemplate"];
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        AvatarInitials.Text = SessionProfile.Initials;
        if (!loaded) await LoadCatalogAsync();
    }

    private async Task LoadCatalogAsync()
    {
        LoadingIndicator.IsRunning = true;
        LoadingIndicator.IsVisible = true;
        CatalogError.IsVisible = false;
        try
        {
            all.Clear();
            foreach (var knife in await CatalogApiClient.Current.GetCatalogAsync()) all.Add(knife);
            loaded = true;
            ApplyFilter();
        }
        catch (ApiException exception)
        {
            CatalogError.Text = exception.Message;
            CatalogError.IsVisible = true;
        }
        finally
        {
            LoadingIndicator.IsRunning = false;
            LoadingIndicator.IsVisible = false;
        }
    }

    private void ApplyFilter()
    {
        var search = SearchBox.Text?.Trim() ?? string.Empty;
        var filtered = all.Where(x =>
            (equipment.Length == 0 || x.Equipment == equipment) &&
            (search.Length == 0 || x.Number.Contains(search, StringComparison.OrdinalIgnoreCase)));
        visible.Clear();
        foreach (var knife in filtered) visible.Add(knife);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void Equipment_Clicked(object sender, EventArgs e)
    {
        if (sender is not Button selected) return;
        equipment = selected.CommandParameter?.ToString() ?? string.Empty;
        SetActiveEquipmentButton(selected);
        ApplyFilter();
    }

    private void SetActiveEquipmentButton(Button selected)
    {
        Button[] buttons =
        [
            AllEquipmentButton,
            BigLeskoButton,
            LabelSourceButton,
            MarkAndyButton,
            NilpeterButton,
            FiguredButton
        ];

        foreach (var button in buttons)
        {
            var isActive = ReferenceEquals(button, selected);
            button.BackgroundColor = Color.FromArgb(isActive ? "#2F5F92" : "#F7F8FA");
            button.TextColor = isActive ? Colors.White : Color.FromArgb("#1F1F1F");
        }
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
        await Shell.Current.GoToAsync($"{nameof(KnifeDetailPage)}?id={knife.Id}");
    }

    private async void Profile_Tapped(object sender, TappedEventArgs e) =>
        await Shell.Current.GoToAsync(nameof(ProfilePage));
}
