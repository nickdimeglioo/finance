using System.Text;
using FinanceTracker.Api.Features.Imports;
using FinanceTracker.Api.Features.Rules;

namespace FinanceTracker.Api.Services;

public sealed class CsvParserService
{
    public async Task<ParsedCsvResult> ParseAsync(IFormFile file, Ruleset ruleset, CancellationToken cancellationToken)
    {
        if (file.Length <= 0)
        {
            throw new ArgumentException("Import file is empty.");
        }

        var sourceConfig = RulesetJson.Read(ruleset.SourceConfig, new RulesetSourceConfigDto(",", "utf-8", []));
        var delimiter = string.IsNullOrEmpty(sourceConfig.Delimiter) ? ',' : sourceConfig.Delimiter[0];
        await using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream, ResolveEncoding(sourceConfig.Encoding), detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        var content = await reader.ReadToEndAsync(cancellationToken);
        var records = ReadRecords(content, delimiter)
            .Where(record => record.Count > 0 && record.Any(value => !string.IsNullOrWhiteSpace(value)))
            .ToList();

        if (records.Count == 0)
        {
            throw new ArgumentException("CSV file does not include a header row.");
        }

        var columns = records[0].Select((column, index) => string.IsNullOrWhiteSpace(column) ? $"Column {index + 1}" : column.Trim()).ToList();
        var normalizedColumns = columns.Select(NormalizeColumnReference).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var errors = new List<ImportJobErrorDto>();

        foreach (var expected in sourceConfig.ExpectedColumns ?? [])
        {
            if (!columns.Contains(expected, StringComparer.OrdinalIgnoreCase)
                && !normalizedColumns.Contains(NormalizeColumnReference(expected)))
            {
                errors.Add(new ImportJobErrorDto(0, expected, "Expected column is missing."));
            }
        }

        var rows = new List<ParsedCsvRow>();
        for (var recordIndex = 1; recordIndex < records.Count; recordIndex++)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var record = records[recordIndex];
            for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                var value = columnIndex < record.Count ? record[columnIndex] : string.Empty;
                var column = columns[columnIndex];
                values[column] = value;
                values.TryAdd(NormalizeColumnReference(column), value);
            }

            rows.Add(new ParsedCsvRow(recordIndex, values));
        }

        return new ParsedCsvResult(columns, rows, errors);
    }

    internal static string NormalizeColumnReference(string value)
    {
        var builder = new StringBuilder();
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static Encoding ResolveEncoding(string? encoding)
    {
        if (string.IsNullOrWhiteSpace(encoding))
        {
            return Encoding.UTF8;
        }

        try
        {
            return Encoding.GetEncoding(encoding);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    private static IEnumerable<List<string>> ReadRecords(string content, char delimiter)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < content.Length; i++)
        {
            var ch = content[i];
            if (inQuotes)
            {
                if (ch == '"' && i + 1 < content.Length && content[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    field.Append(ch);
                }
                continue;
            }

            if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (ch == '\n')
            {
                row.Add(field.ToString().TrimEnd('\r'));
                field.Clear();
                yield return row;
                row = [];
            }
            else
            {
                field.Append(ch);
            }
        }

        row.Add(field.ToString().TrimEnd('\r'));
        yield return row;
    }
}
