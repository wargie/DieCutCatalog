using System.Text;
using System.IO;

namespace DieCutCatalog.Desktop;

internal static class CsvReferenceImportReader
{
    private static readonly HashSet<string> HeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Название", "Наименование", "Значение", "Name", "Value"
    };

    internal static async Task<IReadOnlyList<string>> ReadNamesAsync(string path)
    {
        var content = await File.ReadAllTextAsync(path, Encoding.UTF8);
        return ParseNames(content);
    }

    internal static IReadOnlyList<string> ParseNames(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return [];

        var delimiter = DetectDelimiter(content);
        var rows = ParseRows(content, delimiter)
            .Where(row => row.Any(value => !string.IsNullOrWhiteSpace(value)))
            .ToList();
        if (rows.Count == 0) return [];

        var valueColumn = 0;
        var firstDataRow = 0;
        for (var index = 0; index < rows[0].Count; index++)
        {
            if (!HeaderNames.Contains(rows[0][index].Trim())) continue;
            valueColumn = index;
            firstDataRow = 1;
            break;
        }

        return rows.Skip(firstDataRow)
            .Where(row => row.Count > valueColumn)
            .Select(row => row[valueColumn].Trim())
            .Where(value => value.Length > 0)
            .ToArray();
    }

    private static char DetectDelimiter(string content)
    {
        var commaCount = 0;
        var semicolonCount = 0;
        var inQuotes = false;
        foreach (var character in content)
        {
            if (character == '"') inQuotes = !inQuotes;
            if (inQuotes) continue;
            if (character is '\r' or '\n') break;
            if (character == ',') commaCount++;
            else if (character == ';') semicolonCount++;
        }
        return semicolonCount > commaCount ? ';' : ',';
    }

    private static List<List<string>> ParseRows(string content, char delimiter)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var value = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];
            if (character == '"')
            {
                if (inQuotes && index + 1 < content.Length && content[index + 1] == '"')
                {
                    value.Append('"');
                    index++;
                }
                else inQuotes = !inQuotes;
            }
            else if (character == delimiter && !inQuotes)
            {
                row.Add(value.ToString());
                value.Clear();
            }
            else if (character is '\r' or '\n' && !inQuotes)
            {
                row.Add(value.ToString());
                value.Clear();
                rows.Add(row);
                row = [];
                if (character == '\r' && index + 1 < content.Length && content[index + 1] == '\n') index++;
            }
            else value.Append(character);
        }

        if (value.Length > 0 || row.Count > 0)
        {
            row.Add(value.ToString());
            rows.Add(row);
        }
        return rows;
    }
}
