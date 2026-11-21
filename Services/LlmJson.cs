using System.Text.Json;

namespace AiEmailer.Api.Services;

public static class LlmJson
{
    public static T Parse<T>(string raw)
    {
        var json = raw.Trim();
        // Accept one surrounding Markdown fence without changing JSON string values.
        if (json.StartsWith("```", StringComparison.Ordinal) && json.EndsWith("```", StringComparison.Ordinal))
        {
            var newline = json.IndexOf('\n');
            if (newline >= 0 && newline < json.Length - 3 &&
                json[..newline].Trim() is var fence &&
                (fence == "```" || fence.Equals("```json", StringComparison.OrdinalIgnoreCase)))
                json = json[(newline + 1)..^3].Trim();
        }
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new JsonException("The model returned null JSON.");
    }
}
