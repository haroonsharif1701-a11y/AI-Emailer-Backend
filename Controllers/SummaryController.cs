using System.Text.Json;
using AiEmailer.Api.Models;
using AiEmailer.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiEmailer.Api.Controllers;

[ApiController]
[Route("api/summary")]
[Authorize]
public class SummaryController : ControllerBase
{
    private readonly ILlmProvider _llm;

    public SummaryController(ILlmProvider llm)
    {
        _llm = llm;
    }

    [HttpPost("generate")]
    public async Task<ActionResult<SummarizeResponse>> Generate(
        [FromBody] SummarizeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.EmailBody))
        {
            return BadRequest(new { message = "EmailBody is required." });
        }

        // Ask for strict JSON back so the inbox UI can bind it directly —
        // same structured-output pattern used for classification/threat later.
        var systemPrompt =
            "You summarize emails for a busy professional. Respond with ONLY a JSON object, " +
            "no markdown fences, no commentary, matching exactly this shape: " +
            "{\"summary\": string, \"actionItems\": string[], \"priority\": \"Low\"|\"Normal\"|\"High\"}";

        var userPrompt = $"From: {request.Sender}\nSubject: {request.Subject}\n\n{request.EmailBody}";

        try
        {
            var raw = await _llm.GenerateAsync(systemPrompt, userPrompt, ct);
            var cleaned = raw.Trim().Trim('`').Replace("json\n", "", StringComparison.OrdinalIgnoreCase);

            var parsed = JsonSerializer.Deserialize<SummarizeResponse>(cleaned, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            if (parsed is null)
            {
                return StatusCode(502, new { message = "Model returned unparseable output." });
            }

            return Ok(parsed);
        }
        catch (JsonException)
        {
            return StatusCode(502, new { message = "Model did not return valid JSON. Consider lowering temperature or tightening the prompt." });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { message = ex.Message });
        }
    }
}
