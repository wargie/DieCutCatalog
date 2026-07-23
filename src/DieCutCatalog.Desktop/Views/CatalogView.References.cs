namespace DieCutCatalog.Desktop.Views;

public partial class CatalogView
{
    internal async Task ReloadReferenceDataAsync()
    {
        if (_api is null) return;
        try
        {
            var references = await _api.GetCatalogReferencesAsync();
            var equipment = references.Equipment.Select(x => x.Name).ToArray();
            var materials = references.Materials.Select(x => x.Name).ToArray();
            var figures = references.Figures.Select(x => x.Name).ToArray();
            EquipmentBox.ItemsSource = equipment;
            MaterialBox.ItemsSource = materials;
            FigureBox.ItemsSource = figures;
            SetEquipmentTabs(equipment);
            SetFilterItems(MaterialFilter, materials);
            SetFilterItems(FigureFilter, figures);
        }
        catch (CatalogApiException exception)
        {
            CatalogError.Text = exception.Message;
        }
    }
}
