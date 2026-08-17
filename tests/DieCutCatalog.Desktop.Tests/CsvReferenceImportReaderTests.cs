using Xunit;

namespace DieCutCatalog.Desktop.Tests;

public sealed class CsvReferenceImportReaderTests
{
    [Fact]
    public void ParseNames_ReadsNamedColumnFromSemicolonCsv()
    {
        const string csv = "Код;Название;Комментарий\r\n1;Clear PET30;Основной\r\n2;\"Paper, Premium\";\"С запятой\"";

        var names = CsvReferenceImportReader.ParseNames(csv);

        Assert.Equal(["Clear PET30", "Paper, Premium"], names);
    }

    [Fact]
    public void ParseNames_UsesFirstColumnWhenHeaderIsAbsent()
    {
        const string csv = "Материал A,описание\n\"Материал \"\"B\"\"\",текст\n\n";

        var names = CsvReferenceImportReader.ParseNames(csv);

        Assert.Equal(["Материал A", "Материал \"B\""], names);
    }
}
