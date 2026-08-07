using DieCutCatalog.Api;
using DieCutCatalog.Application.Catalog;
using DieCutCatalog.Domain.Catalog;

namespace DieCutCatalog.Infrastructure.Tests;

public sealed class SaveDieCutRequestTests
{
    [Fact]
    public void ToCommand_PreservesJustCutPrice()
    {
        var calculatedAt = new DateTimeOffset(2026, 8, 7, 6, 0, 0, TimeSpan.Zero);
        var price = new JustCutPriceResult(12_345.67m, "RUB", true, 998877, calculatedAt, "Test");
        var request = new SaveDieCutRequest(
            "001", null, "Nilpeter/Lesko", 106, 50, 40, 2, 3, 3, 2,
            "Бумага", 0.48m, "Прямоугольник", null, null, DieCutStatus.Active, price);

        var command = request.ToCommand();

        Assert.Same(price, command.JustCutPrice);
    }
}