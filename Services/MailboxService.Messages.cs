using AiEmailer.Api.Models;

namespace AiEmailer.Api.Services;

public sealed partial class MailboxService
{
    public async Task<MailSyncResult> SyncAsync(MailboxOwner owner, CancellationToken ct)
    {
        var connection = await ConnectionAsync(owner, ct);
        if (!await repository.CanReadAsync(owner, ct)) throw new MailboxException(403, "Inbox permission is required.");
        var result = await provider.SyncAsync(connection, ct);
        await repository.SaveSyncAsync(connection, result.HistoryId, ct);
        return result;
    }
    public async Task<string> SendAsync(MailboxOwner owner, SendMailRequest request, CancellationToken ct) =>
        await provider.SendAsync(await ConnectionAsync(owner, ct), request, ct);
    public async Task ModifyAsync(MailboxOwner owner, string id, string action, CancellationToken ct) =>
        await provider.ModifyAsync(await ConnectionAsync(owner, ct), id, action, ct);
    public async Task<MailPage> ListAsync(MailboxOwner owner, int pageSize, string? pageToken, string? query, CancellationToken ct) =>
        await provider.ListAsync(await ConnectionAsync(owner, ct), pageSize, pageToken, query, ct);
    public async Task<MailMessage> GetAsync(MailboxOwner owner, string id, CancellationToken ct) =>
        await provider.GetAsync(await ConnectionAsync(owner, ct), id, ct);
    public async Task<MailFile> AttachmentAsync(MailboxOwner owner, string id, string attachmentId, CancellationToken ct) =>
        await provider.AttachmentAsync(await ConnectionAsync(owner, ct), id, attachmentId, ct);
}
