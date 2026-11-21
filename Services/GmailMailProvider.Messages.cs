using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AiEmailer.Api.Models;
using Microsoft.AspNetCore.WebUtilities;

namespace AiEmailer.Api.Services;

public sealed partial class GmailMailProvider
{
    public const int MaxAttachmentBytes = 10 * 1024 * 1024;
    private static string Segment(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 512) throw new MailboxException(400, "Invalid message identifier.");
        return Uri.EscapeDataString(id);
    }

    public async Task<MailPage> ListAsync(MailboxConnection connection, int pageSize, string? pageToken, string? query, CancellationToken ct)
    {
        if (pageSize is < 1 or > 50 || pageToken?.Length > 4096 || query?.Length > 500)
            throw new MailboxException(400, "Use a page size of 1–50 and a search query of at most 500 characters.");
        var path = QueryHelpers.AddQueryString("messages", new Dictionary<string, string?>
        {
            ["maxResults"] = pageSize.ToString(), ["pageToken"] = pageToken, ["q"] = string.IsNullOrWhiteSpace(query) ? "in:inbox" : query
        });
        var result = await MailAsync(connection, HttpMethod.Get, path, null, ct);
        var ids = (result["messages"]?.AsArray() ?? new JsonArray()).Select(message => Segment(Text(message, "id"))).ToArray();
        var messages = new MailMessage[ids.Length];
        await Parallel.ForEachAsync(Enumerable.Range(0, ids.Length), new ParallelOptions
        { MaxDegreeOfParallelism = 5, CancellationToken = ct }, async (index, token) =>
        {
            var data = await MailAsync(connection, HttpMethod.Get, "messages/" + ids[index] + "?format=metadata", null, token);
            messages[index] = Parse(data);
        });
        return new(messages, result["nextPageToken"]?.GetValue<string>());
    }

    public async Task<MailMessage> GetAsync(MailboxConnection connection, string id, CancellationToken ct)
    {
        var data = await FullAsync(connection, id, ct);
        foreach (var part in Parts(data["payload"]))
        {
            if (Text(part, "filename") != "" || Text(part["body"], "attachmentId") == "" ||
                Text(part, "mimeType") is not ("text/plain" or "text/html")) continue;
            if ((part["body"]?["size"]?.GetValue<long>() ?? 0) > 1024 * 1024) continue;
            var body = await MailAsync(connection, HttpMethod.Get, "messages/" + Segment(id) + "/attachments/" +
                Segment(Text(part["body"], "attachmentId")), null, ct);
            part["body"] = body.DeepClone();
        }
        return Parse(data);
    }

    private Task<JsonObject> FullAsync(MailboxConnection connection, string id, CancellationToken ct) =>
        MailAsync(connection, HttpMethod.Get, "messages/" + Segment(id) + "?format=full", null, ct);

    public async Task<MailFile> AttachmentAsync(MailboxConnection connection, string id, string attachmentId, CancellationToken ct)
    {
        var data = await FullAsync(connection, id, ct);
        var part = Parts(data["payload"]).FirstOrDefault(part => Text(part, "partId") == attachmentId && Text(part, "filename") != "")
            ?? throw new MailboxException(404, "Attachment was not found in this message.");
        if ((part["body"]?["size"]?.GetValue<long>() ?? 0) > MaxAttachmentBytes)
            throw new MailboxException(413, "Attachment exceeds the 10 MB limit.");
        var body = part["body"];
        if (Text(body, "attachmentId") != "") body = await MailAsync(connection, HttpMethod.Get,
            "messages/" + Segment(id) + "/attachments/" + Segment(Text(body, "attachmentId")), null, ct);
        var bytes = Decode(Text(body, "data"));
        if (bytes.Length > MaxAttachmentBytes) throw new MailboxException(413, "Attachment exceeds the 10 MB limit.");
        var fileName = Path.GetFileName(Text(part, "filename").Replace('\\', '/'));
        return new(string.IsNullOrWhiteSpace(fileName) ? "attachment" : fileName, bytes);
    }

    private static byte[] Decode(string data)
    {
        try { return WebEncoders.Base64UrlDecode(data); }
        catch (FormatException) { throw new MailboxException(502, "Gmail returned invalid message data."); }
    }

    private static IEnumerable<JsonNode> Parts(JsonNode? part)
    {
        if (part is null) yield break;
        yield return part;
        foreach (var child in part["parts"]?.AsArray() ?? new JsonArray())
            foreach (var nested in Parts(child)) yield return nested;
    }

    private static string Header(JsonNode? payload, string name) =>
        payload?["headers"]?.AsArray().FirstOrDefault(header => string.Equals(Text(header, "name"), name, StringComparison.OrdinalIgnoreCase))?["value"]?.GetValue<string>() ?? "";

    private static string BodyText(JsonNode part)
    {
        var bytes = Decode(Text(part["body"], "data"));
        var charset = Regex.Match(Header(part, "Content-Type"), "charset=[\"']?([^;\\s\"']+)", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        Encoding encoding = Encoding.UTF8;
        if (charset.Success)
        {
            try { encoding = Encoding.GetEncoding(charset.Groups[1].Value); }
            catch (ArgumentException) { }
        }
        return encoding.GetString(bytes);
    }

    private static MailMessage Parse(JsonNode data)
    {
        var payload = data["payload"];
        var parts = Parts(payload).ToArray();
        var plain = parts.Where(part => Text(part, "filename") == "" && Text(part, "mimeType") == "text/plain").Select(BodyText);
        var text = string.Join("\n", plain);
        if (string.IsNullOrWhiteSpace(text))
        {
            var html = string.Join("\n", parts.Where(part => Text(part, "filename") == "" && Text(part, "mimeType") == "text/html").Select(BodyText));
            text = WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]*>", " ", RegexOptions.Singleline, TimeSpan.FromSeconds(1)));
        }
        var attachments = parts.Where(part => Text(part, "filename") != "").Select(part => new MailAttachment(
            Text(part, "partId"), Text(part, "filename"), Text(part, "mimeType"), part["body"]?["size"]?.GetValue<long>() ?? 0)).ToArray();
        long.TryParse(Text(data, "internalDate"), out var milliseconds);
        return new(Text(data, "id"), Text(data, "threadId"), Header(payload, "From"), Header(payload, "To"),
            Header(payload, "Subject"), DateTimeOffset.FromUnixTimeMilliseconds(milliseconds), WebUtility.HtmlDecode(Text(data, "snippet")),
            text, data["labelIds"]?.AsArray().Select(label => label!.GetValue<string>()).ToArray() ?? [], attachments,
            Header(payload, "Message-ID"), Header(payload, "References"));
    }
}
