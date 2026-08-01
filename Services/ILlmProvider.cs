namespace AiEmailer.Api.Services;

/// <summary>
/// Every AI feature (reply generation, summarization, classification, search)
/// talks to this interface only — never to a concrete provider. Swapping
/// Ollama for OpenAI, or later moving inference into the separate Python
/// AI service the plan describes, means changing one DI registration in
/// Program.cs, not touching any controller.
/// </summary>
public interface ILlmProvider
{
    Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}
