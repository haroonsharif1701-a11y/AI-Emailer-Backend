using System.Net.Http.Headers;
using System.Text.Json.Serialization;

namespace AiEmailer.Api.Services;

public class OpenAiLlmProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly string _model;

    public OpenAiLlmProvider(HttpClient http, IConfiguration config)
    {
        var baseUrl = config["Llm:OpenAI:BaseUrl"] ?? "https://api.openai.com/v1";
        var apiKey = config["Llm:OpenAI:ApiKey"];
        _model = config["Llm:OpenAI:Model"] ?? "gpt-4.1";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Llm:OpenAI:ApiKey is not set. Use `dotnet user-secrets set \"Llm:OpenAI:ApiKey\" \"sk-...\"` " +
                "rather than committing it to appsettings.json.");
        }

        _http = http;
        _http.BaseAddress = new Uri(baseUrl);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    public async Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var requestBody = new OpenAiChatRequest
        {
            Model = _model,
            Messages = new[]
            {
                new OpenAiMessage { Role = "system", Content = systemPrompt },
                new OpenAiMessage { Role = "user", Content = userPrompt },
            },
        };

        using var response = await _http.PostAsJsonAsync("/chat/completions", requestBody, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"OpenAI request failed ({(int)response.StatusCode}): {body}");
        }

        var parsed = await response.Content.ReadFromJsonAsync<OpenAiChatResponse>(cancellationToken: ct);
        return parsed?.Choices?.FirstOrDefault()?.Message?.Content?.Trim()
            ?? throw new InvalidOperationException("OpenAI returned an empty response.");
    }

    private class OpenAiChatRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("messages")] public OpenAiMessage[] Messages { get; set; } = Array.Empty<OpenAiMessage>();
    }

    private class OpenAiMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
    }

    private class OpenAiChoice
    {
        [JsonPropertyName("message")] public OpenAiMessage? Message { get; set; }
    }

    private class OpenAiChatResponse
    {
        [JsonPropertyName("choices")] public OpenAiChoice[]? Choices { get; set; }
    }
}
