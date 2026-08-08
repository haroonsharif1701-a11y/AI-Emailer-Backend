using System.Text.Json;
using AiEmailer.Api.Models;
using AiEmailer.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiEmailer.Api.Controllers;

[ApiController]
[Route("api/attachment")]
[Authorize]
public class AttachmentController : ControllerBase
{
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
    private const int MaxCharsSentToModel = 6000; // keeps local-model latency reasonable

    private static readonly string[] AllowedExtensions = { ".pdf", ".docx", ".xlsx", ".png", ".jpg", ".jpeg" };

    private readonly IAttachmentTextExtractor _extractor;
    private readonly ILlmProvider _llm;
    private readonly ILogger<AttachmentController> _logger;

    public AttachmentController(IAttachmentTextExtractor extractor, ILlmProvider llm, ILogger<AttachmentController> logger)
    {
        _extractor = extractor;
        _llm = llm;
        _logger = logger;
    }

    [HttpPost("analyze")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    public async Task<ActionResult<AttachmentAnalysisResponse>> Analyze(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "No file was uploaded." });
        }

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
        {
            return BadRequest(new { message = $"Unsupported file type '{ext}'. Supported: {string.Join(", ", AllowedExtensions)}" });
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return BadRequest(new { message = $"File exceeds the {MaxFileSizeBytes / 1024 / 1024}MB limit." });
        }

        string extractedText;
        try
        {
            await using var stream = file.OpenReadStream();
            extractedText = await _extractor.ExtractTextAsync(stream, file.FileName);
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract text from {FileName}", file.FileName);
            return BadRequest(new { message = $"Couldn't read '{file.FileName}' — the file may be corrupted or password-protected." });
        }

        if (string.IsNullOrWhiteSpace(extractedText))
        {
            return BadRequest(new { message = "No readable text found in this file." });
        }

        var truncated = extractedText.Length > MaxCharsSentToModel;
        var textForModel = truncated ? extractedText[..MaxCharsSentToModel] : extractedText;
        if (truncated)
        {
            _logger.LogInformation(
                "Truncated {FileName} from {Original} to {Truncated} chars before sending to the model",
                file.FileName, extractedText.Length, MaxCharsSentToModel);
        }

        var systemPrompt =
            "You analyze document contents for a business email assistant. Respond with ONLY a JSON object, " +
            "no markdown fences, no commentary, matching exactly this shape: " +
            "{\"summary\": string, \"threatLevel\": \"Low\"|\"Medium\"|\"High\", \"threatNotes\": string}. " +
            "threatLevel should reflect signs of malicious content, scams, or social engineering in the " +
            "document text itself — not general business risk. Most ordinary business documents are Low.";

        var userPrompt = $"Filename: {file.FileName}\n\nDocument text:\n{textForModel}";

        try
        {
            var raw = await _llm.GenerateAsync(systemPrompt, userPrompt, ct);
            var cleaned = raw.Trim().Trim('`').Replace("json\n", "", StringComparison.OrdinalIgnoreCase);

            var parsed = JsonSerializer.Deserialize<AttachmentAnalysisResponse>(cleaned, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            if (parsed is null)
            {
                return StatusCode(502, new { message = "Model returned unparseable output." });
            }

            parsed.FileName = file.FileName;
            parsed.FileType = ext.TrimStart('.').ToUpperInvariant();

            return Ok(parsed);
        }
        catch (JsonException)
        {
            return StatusCode(502, new { message = "Model did not return valid JSON for this attachment." });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { message = ex.Message });
        }
    }
}
