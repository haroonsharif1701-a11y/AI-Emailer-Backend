using System.Text.Json.Nodes;
using AiEmailer.Api.Models;
using Microsoft.AspNetCore.WebUtilities;

namespace AiEmailer.Api.Services;

public sealed partial class GmailMailProvider
{
    public async Task<MailSyncResult> SyncAsync(MailboxConnection connection, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(connection.HistoryId)) return await ResetAsync(connection, ct);
        string? pageToken = null;
        var changes = 0;
        var historyId = connection.HistoryId;
        try
        {
            do
            {
                var path = QueryHelpers.AddQueryString("history", new Dictionary<string, string?>
                { ["startHistoryId"] = connection.HistoryId, ["maxResults"] = "500", ["pageToken"] = pageToken });
                var result = await MailAsync(connection, HttpMethod.Get, path, null, ct);
                changes += result["history"]?.AsArray().Count ?? 0;
                historyId = Required(result, "historyId");
                pageToken = result["nextPageToken"]?.GetValue<string>();
            } while (!string.IsNullOrEmpty(pageToken));
        }
        catch (MailboxException ex) when (ex.Status == 404) { return await ResetAsync(connection, ct); }
        return new(historyId, false, changes);
    }

    private async Task<MailSyncResult> ResetAsync(MailboxConnection connection, CancellationToken ct)
    {
        var profile = await MailAsync(connection, HttpMethod.Get, "profile", null, ct);
        // Messages are fetched live; a reset tells clients to reload their current page.
        return new(Required(profile, "historyId"), true, 0);
    }
}
