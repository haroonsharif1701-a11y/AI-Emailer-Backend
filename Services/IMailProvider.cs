using AiEmailer.Api.Models;

namespace AiEmailer.Api.Services;

public interface IMailProvider
{
    string AuthorizationUrl(string state, string verifier);
    Task<MailboxConnection> ConnectAsync(MailboxOwner owner, string code, string verifier, CancellationToken ct);
    Task<bool> RevokeAsync(MailboxConnection connection, CancellationToken ct);
    Task<MailPage> ListAsync(MailboxConnection connection, int pageSize, string? pageToken, string? query, CancellationToken ct);
    Task<MailMessage> GetAsync(MailboxConnection connection, string id, CancellationToken ct);
    Task<MailFile> AttachmentAsync(MailboxConnection connection, string id, string attachmentId, CancellationToken ct);
    Task<string> SendAsync(MailboxConnection connection, SendMailRequest request, CancellationToken ct);
    Task ModifyAsync(MailboxConnection connection, string id, string action, CancellationToken ct);
    Task<MailSyncResult> SyncAsync(MailboxConnection connection, CancellationToken ct);
}
