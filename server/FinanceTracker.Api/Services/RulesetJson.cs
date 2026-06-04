using System.Text.Json;

namespace FinanceTracker.Api.Services;

internal static class RulesetJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    internal static T Read<T>(string? json, T fallback)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return fallback;
        }

        return JsonSerializer.Deserialize<T>(json, Options) ?? fallback;
    }

    internal static string Write<T>(T value)
    {
        return JsonSerializer.Serialize(value, Options);
    }
}
