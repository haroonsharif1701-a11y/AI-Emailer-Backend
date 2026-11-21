using AiEmailer.Api.Models;

namespace AiEmailer.Api.Services;

public interface IMailboxRepository
{
    Task<MailboxConnection?> GetAsync(MailboxOwner owner, CancellationToken ct);
    Task SaveAsync(MailboxConnection connection, CancellationToken ct);
    Task<MailboxConnection?> DeleteAsync(MailboxOwner owner, CancellationToken ct);
    Task MarkReconnectAsync(MailboxConnection connection, CancellationToken ct);
    Task SaveSyncAsync(MailboxConnection connection, string historyId, CancellationToken ct);
    Task<IReadOnlyList<MailboxOwner>> ListOwnersAsync(CancellationToken ct);
    Task<bool> CanReadAsync(MailboxOwner owner, CancellationToken ct);
    Task CreateAuthorizationAsync(string state, string browserNonce, MailboxAuthorization authorization, CancellationToken ct);
    Task<MailboxAuthorization?> ConsumeAuthorizationAsync(string state, string browserNonce, CancellationToken ct);
}
