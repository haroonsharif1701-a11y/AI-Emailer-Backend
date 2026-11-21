using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AiEmailer.Api.Models;
using Microsoft.AspNetCore.WebUtilities;

namespace AiEmailer.Api.Services;

public sealed partial class MailboxService(IMailboxRepository repository, IMailProvider provider,
    ILoginSessionService sessions, MailProviderSettings settings)
{
    public static string RandomCode() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    private void Enabled()
    {
        if (!settings.Gmail) throw new MailboxException(403, "Gmail is disabled.");
    }

    public async Task<string> ConnectAsync(MailboxOwner owner, Guid session, string browserNonce, CancellationToken ct)
    {
        Enabled();
        var state = RandomCode();
        var verifier = RandomCode();
        var url = provider.AuthorizationUrl(state, verifier);
        await repository.CreateAuthorizationAsync(state, browserNonce, new(owner, session, verifier), ct);
        return url;
    }

    public async Task CompleteAsync(string? state, string? browserNonce, string? code, string? error, CancellationToken ct)
    {
        Enabled();
        if (!ValidCode(state)) throw new MailboxException(400, "Invalid mailbox connection state. Start Connect Gmail again from AiEmailer.");
        if (!ValidCode(browserNonce)) throw new MailboxException(400, "The mailbox connection cookie is missing or expired. Open the frontend using the same site as the API (localhost for local development), allow cookies, and start Connect Gmail again.");
        var pending = await repository.ConsumeAuthorizationAsync(state!, browserNonce!, ct)
            ?? throw new MailboxException(400, "Connection request expired or was already used. Connect again.");
        if (!await sessions.IsActiveAsync(pending.SessionId, pending.Owner.UserId, pending.Owner.OrgId, ct) ||
            !await repository.CanReadAsync(pending.Owner, ct)) throw new MailboxException(403, "Your session or inbox permission is no longer active.");
        if (!string.IsNullOrEmpty(error)) throw new MailboxException(400, "Google consent was cancelled. You can connect again.");
        if (string.IsNullOrEmpty(code) || code.Length > 4096) throw new MailboxException(400, "Google authorization code is missing or invalid.");
        var connection = await provider.ConnectAsync(pending.Owner, code, pending.Verifier, ct);
        if (!await sessions.IsActiveAsync(pending.SessionId, pending.Owner.UserId, pending.Owner.OrgId, ct) ||
            !await repository.CanReadAsync(pending.Owner, ct)) throw new MailboxException(403, "Your session or inbox permission is no longer active.");
        await repository.SaveAsync(connection, ct);
    }

    public async Task<MailboxStatus> StatusAsync(MailboxOwner owner, CancellationToken ct)
    {
        Enabled();
        var connection = await repository.GetAsync(owner, ct);
        return new(connection is not null, connection?.Email, connection?.ReconnectRequired ?? false, connection?.SyncedAt);
    }

    public async Task<bool> DisconnectAsync(MailboxOwner owner, CancellationToken ct)
    {
        Enabled();
        var connection = await repository.DeleteAsync(owner, ct);
        return connection is null || await provider.RevokeAsync(connection, ct);
    }

    private async Task<MailboxConnection> ConnectionAsync(MailboxOwner owner, CancellationToken ct)
    {
        Enabled();
        return await repository.GetAsync(owner, ct) ?? throw new MailboxException(409, "Connect Gmail first.");
    }

    private static bool ValidCode(string? value) => value is not null && Regex.IsMatch(value, "^[A-Za-z0-9_-]{43}$");
}
