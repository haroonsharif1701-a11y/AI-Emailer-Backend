using AiEmailer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AiEmailer.Api.Controllers;

public sealed partial class MailboxController
{
    [HttpPost("messages/{id}/summary"), MenuPermission("view", "inbox")]
    public async Task<IActionResult> Summary(string id, [FromServices] MailboxAiService ai, CancellationToken ct) =>
        Ok(await ai.SummarizeAsync(Owner, id, ct));

    [HttpPost("messages/{id}/reply-draft"), MenuPermission("view", "inbox")]
    [MenuPermission("add", "reply")]
    public async Task<IActionResult> ReplyDraft(string id, [FromServices] MailboxAiService ai, CancellationToken ct) =>
        Ok(await ai.ReplyAsync(Owner, id, ct));

    [HttpPost("messages/{id}/attachments/{attachmentId}/analysis"), MenuPermission("view", "inbox")]
    [MenuPermission("add", "attachments")]
    public async Task<IActionResult> Analysis(string id, string attachmentId, [FromServices] MailboxAiService ai, CancellationToken ct) =>
        Ok(await ai.AnalyzeAsync(Owner, id, attachmentId, ct));
}
