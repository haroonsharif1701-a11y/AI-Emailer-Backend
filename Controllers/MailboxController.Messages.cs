using AiEmailer.Api.Services;
using AiEmailer.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace AiEmailer.Api.Controllers;

public sealed partial class MailboxController
{
    [HttpPost("sync"), MenuPermission("view", "inbox")]
    public async Task<IActionResult> Sync(CancellationToken ct) => Ok(await mailbox.SyncAsync(Owner, ct));
    [HttpPost("messages/send"), MenuPermission("add", "inbox")]
    [RequestSizeLimit(512000)]
    public async Task<IActionResult> Send(SendMailRequest request, CancellationToken ct) =>
        Ok(new { id = await mailbox.SendAsync(Owner, request, ct) });

    [HttpPatch("messages/{id}"), MenuPermission("update", "inbox")]
    public async Task<IActionResult> Modify(string id, MailActionRequest request, CancellationToken ct)
    {
        if (request.Action == "trash") return BadRequest(new { message = "Use the trash endpoint." });
        await mailbox.ModifyAsync(Owner, id, request.Action, ct);
        return NoContent();
    }

    [HttpDelete("messages/{id}"), MenuPermission("delete", "inbox")]
    public async Task<IActionResult> Trash(string id, CancellationToken ct)
    {
        await mailbox.ModifyAsync(Owner, id, "trash", ct);
        return NoContent();
    }

    [HttpGet("messages"), MenuPermission("view", "inbox")]
    public async Task<IActionResult> Messages(CancellationToken ct, int pageSize = 25, string? pageToken = null, string? query = null) =>
        Ok(await mailbox.ListAsync(Owner, pageSize, pageToken, query, ct));

    [HttpGet("messages/{id}"), MenuPermission("view", "inbox")]
    public async Task<IActionResult> Message(string id, CancellationToken ct) => Ok(await mailbox.GetAsync(Owner, id, ct));

    [HttpGet("messages/{id}/attachments/{attachmentId}"), MenuPermission("view", "inbox")]
    public async Task<IActionResult> Attachment(string id, string attachmentId, CancellationToken ct)
    {
        var file = await mailbox.AttachmentAsync(Owner, id, attachmentId, ct);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(file.Content, "application/octet-stream", file.FileName);
    }
}
