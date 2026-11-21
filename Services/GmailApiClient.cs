using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace AiEmailer.Api.Services;

public sealed class GmailApiClient(HttpClient http)
{
    public async Task<JsonObject> RequestAsync(HttpMethod method, string url, string? token, object? body, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, url);
            if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is Dictionary<string, string> form) request.Content = new FormUrlEncodedContent(form);
            else if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await http.SendAsync(request, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);
            var rateLimited = (int)response.StatusCode == 429 || ((int)response.StatusCode == 403 &&
                (raw.Contains("rateLimitExceeded", StringComparison.Ordinal) || raw.Contains("userRateLimitExceeded", StringComparison.Ordinal)));
            if (method == HttpMethod.Get && attempt < 2 && (rateLimited || (int)response.StatusCode >= 500))
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
                if (delay > TimeSpan.FromSeconds(30)) throw new MailboxException(429, "Mail provider is busy. Try later.");
                await Task.Delay(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, ct);
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                var status = raw.Contains("invalid_grant", StringComparison.Ordinal) || (int)response.StatusCode == 401 ? 409 :
                    rateLimited ? 429 : (int)response.StatusCode == 404 ? 404 : (int)response.StatusCode == 403 ? 403 : 502;
                throw new MailboxException(status, status switch
                {
                    409 => "Mailbox authorization expired or was revoked. Reconnect Gmail.",
                    404 => "Mail resource was not found.",
                    403 => "Google denied access. Check Gmail permissions and API configuration.",
                    429 => "Gmail rate limit reached. Try later.",
                    _ => "Google could not complete the request. For sending, check Sent before retrying."
                });
            }
            return string.IsNullOrWhiteSpace(raw) ? new() : JsonNode.Parse(raw)?.AsObject()
                ?? throw new MailboxException(502, "Google returned an invalid response.");
        }
    }
}
