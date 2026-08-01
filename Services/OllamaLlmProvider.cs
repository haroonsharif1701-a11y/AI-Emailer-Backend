using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiEmailer.Api.Services;

public class OllamaLlmProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly ILogger<OllamaLlmProvider> _logger;

    public OllamaLlmProvider(HttpClient http, IConfiguration config, ILogger<OllamaLlmProvider> logger)
    {
        var baseUrl = config["Llm:Ollama:BaseUrl"] ?? "http://localhost:11434";
        _model = config["Llm:Ollama:Model"] ?? "llama3.1";
        _http = http;
        _http.BaseAddress = new Uri(baseUrl);
        _http.Timeout = TimeSpan.FromSeconds(120);
        _logger = logger;
    }

    public async Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation(
            "Ollama request -> model={Model} promptChars={Chars}", _model, userPrompt.Length);

        var requestBody = new OllamaChatRequest
        {
            Model = _model,
            Stream = false,
            Messages = new[]
            {
                new OllamaMessage { Role = "system", Content = systemPrompt },
                new OllamaMessage { Role = "user", Content = userPrompt },
            },
        };

        using var response = await _http.PostAsJsonAsync("/api/chat", requestBody, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "Ollama request FAILED after {ElapsedMs}ms -> status={Status} body={Body}",
                sw.ElapsedMilliseconds, (int)response.StatusCode, body);
            throw new InvalidOperationException(
                $"Ollama request failed ({(int)response.StatusCode}). Is `ollama serve` running and is the " +
                $"model pulled (`ollama pull {_model}`)? Response: {body}");
        }

        var parsed = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken: ct);
        var content = parsed?.Message?.Content?.Trim()
            ?? throw new InvalidOperationException("Ollama returned an empty response.");

        _logger.LogInformation(
            "Ollama request OK after {ElapsedMs}ms -> responseChars={Chars}", sw.ElapsedMilliseconds, content.Length);

        return content;
    }

    private class OllamaChatRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("stream")] public bool Stream { get; set; }
        [JsonPropertyName("messages")] public OllamaMessage[] Messages { get; set; } = Array.Empty<OllamaMessage>();
    }

    private class OllamaMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
    }

    private class OllamaChatResponse
    {
        [JsonPropertyName("message")] public OllamaMessage? Message { get; set; }
    }
}
