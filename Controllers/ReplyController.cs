using AiEmailer.Api.Models;
using AiEmailer.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiEmailer.Api.Controllers;

[ApiController]
[Route("api/reply")]
[Authorize]
public class ReplyController : ControllerBase
{
    private readonly ILlmProvider _llm;

    public ReplyController(ILlmProvider llm)
    {
        _llm = llm;
    }

    [HttpPost("generate")]
    [MenuPermission("add", "reply")]
    public async Task<ActionResult<GenerateReplyResponse>> Generate(
        [FromBody] GenerateReplyRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.OriginalEmailBody))
        {
            return BadRequest(new { message = "OriginalEmailBody is required." });
        }

        var systemPrompt =
            "You are an email assistant drafting a reply on behalf of the user. " +
            $"Write in a {request.Tone} tone. Keep it concise, do not invent facts or " +
            "commitments that are not implied by the original email, and sign off naturally. " +
            "Return only the reply body — no subject line, no commentary.";

        var userPrompt =
            $"Original email" + (string.IsNullOrWhiteSpace(request.SenderName) ? "" : $" from {request.SenderName}") +
            $":\n\n{request.OriginalEmailBody}\n\nWrite the reply.";

        try
        {
            var draft = await _llm.GenerateAsync(systemPrompt, userPrompt, ct);
            return Ok(new GenerateReplyResponse(draft));
        }
        catch (InvalidOperationException ex)
        {
            // Surfaces "Ollama not running" / "OpenAI key missing" style errors
            // as a clean 502 instead of an unhandled 500.
            return StatusCode(502, new { message = ex.Message });
        }
    }
}
