using MailAddress = System.Net.Mail.MailAddress;
using System.Text;
using System.Text.RegularExpressions;
using AiEmailer.Api.Models;
using Microsoft.AspNetCore.WebUtilities;

namespace AiEmailer.Api.Services;

public sealed partial class GmailMailProvider
{
    private static string Address(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 320 || value.Any(ch => ch > 127 || char.IsControl(ch)) ||
            !MailAddress.TryCreate(value, out var address)) throw new MailboxException(400, "A valid email address is required.");
        return address.Address;
    }

    private static string EncodedSubject(string value)
    {
        if (value.Length > 998 || value.Any(char.IsControl)) throw new MailboxException(400, "Invalid subject header.");
        var words = new List<string>();
        var chunk = new StringBuilder();
        foreach (var rune in value.EnumerateRunes())
        {
            if (Encoding.UTF8.GetByteCount(chunk.ToString()) + rune.Utf8SequenceLength > 42)
            {
                words.Add("=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(chunk.ToString())) + "?=");
                chunk.Clear();
            }
            chunk.Append(rune);
        }
        if (chunk.Length > 0) words.Add("=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(chunk.ToString())) + "?=");
        return string.Join("\r\n ", words);
    }

    public async Task<string> SendAsync(MailboxConnection connection, SendMailRequest request, CancellationToken ct)
    {
        var recipient = Address(request.To);
        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > 100000 || string.IsNullOrWhiteSpace(request.Subject))
            throw new MailboxException(400, "Subject and body are required; body must not exceed 100000 characters.");
        var subject = EncodedSubject(request.Subject);
        MailMessage? original = null;
        var headers = "";
        if (!string.IsNullOrEmpty(request.ReplyToMessageId))
        {
            original = await GetAsync(connection, request.ReplyToMessageId, ct);
            var messageId = original.InternetMessageId ?? "";
            if (!Regex.IsMatch(messageId, "^<[^<>\\s]+@[^<>\\s]+>$", RegexOptions.None, TimeSpan.FromSeconds(1)))
                throw new MailboxException(400, "This email has no valid Message-ID for a threaded reply.");
            subject = EncodedSubject(original.Subject);
            headers = "In-Reply-To: " + messageId + "\r\nReferences: " + messageId + "\r\n";
        }
        var body = request.Body.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
        var mime = $"From: {Address(connection.Email)}\r\nTo: {recipient}\r\nSubject: {subject}\r\n" + headers +
            "MIME-Version: 1.0\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n" +
            Convert.ToBase64String(Encoding.UTF8.GetBytes(body), Base64FormattingOptions.InsertLineBreaks);
        var payload = new System.Text.Json.Nodes.JsonObject { ["raw"] = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(mime)) };
        if (original is not null) payload["threadId"] = original.ThreadId;
        var result = await MailAsync(connection, HttpMethod.Post, "messages/send", payload, ct);
        return Required(result, "id");
    }

    public async Task ModifyAsync(MailboxConnection connection, string id, string action, CancellationToken ct)
    {
        if (action == "trash")
        {
            await MailAsync(connection, HttpMethod.Post, "messages/" + Segment(id) + "/trash", new { }, ct);
            return;
        }
        var (add, remove) = action switch
        {
            "read" => (Array.Empty<string>(), new[] { "UNREAD" }),
            "unread" => (new[] { "UNREAD" }, Array.Empty<string>()),
            "archive" => (Array.Empty<string>(), new[] { "INBOX" }),
            _ => throw new MailboxException(400, "Unsupported mailbox action.")
        };
        await MailAsync(connection, HttpMethod.Post, "messages/" + Segment(id) + "/modify", new { addLabelIds = add, removeLabelIds = remove }, ct);
    }
}
