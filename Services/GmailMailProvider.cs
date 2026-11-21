using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AiEmailer.Api.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;

namespace AiEmailer.Api.Services;

public sealed partial class GmailMailProvider(GmailApiClient api, IConfiguration config, IMailboxRepository repository,
    IMemoryCache cache) : IMailProvider
{
    public const string MailScope = "https://www.googleapis.com/auth/gmail.modify";
    private const string ApiRoot = "https://gmail.googleapis.com/gmail/v1/users/me/";
    private string Setting(string name) => config[name] ?? throw new MailboxException(503, $"Configure {name} before connecting Gmail.");
    private string Callback => Setting("Mailbox:Gmail:RedirectUri");

    public string AuthorizationUrl(string state, string verifier)
    {
        if (!Uri.TryCreate(Callback, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
            throw new MailboxException(503, "Configure a valid HTTPS Gmail callback URL.");
        return QueryHelpers.AddQueryString("https://accounts.google.com/o/oauth2/v2/auth", new Dictionary<string, string?>
        {
            ["client_id"] = Setting("Authentication:Google:ClientId"), ["redirect_uri"] = Callback,
            ["response_type"] = "code", ["scope"] = "openid email " + MailScope, ["access_type"] = "offline",
            ["prompt"] = "consent select_account", ["state"] = state, ["code_challenge_method"] = "S256",
            ["code_challenge"] = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
        });
    }

    private Task<JsonObject> TokensAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        form["client_id"] = Setting("Authentication:Google:ClientId");
        form["client_secret"] = Setting("Authentication:Google:ClientSecret");
        return api.RequestAsync(HttpMethod.Post, "https://oauth2.googleapis.com/token", null, form, ct);
    }

    public async Task<MailboxConnection> ConnectAsync(MailboxOwner owner, string code, string verifier, CancellationToken ct)
    {
        var tokens = await TokensAsync(new() { ["grant_type"] = "authorization_code", ["code"] = code,
            ["code_verifier"] = verifier, ["redirect_uri"] = Callback }, ct);
        var scope = Text(tokens, "scope");
        if (!scope.Split(' ').Contains(MailScope)) throw new MailboxException(403, "Grant Gmail access to connect the mailbox.");
        var access = Required(tokens, "access_token");
        var identity = await api.RequestAsync(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo", access, null, ct);
        if (identity["email_verified"]?.GetValue<bool>() != true) throw new MailboxException(403, "A verified Google email is required.");
        var profile = await api.RequestAsync(HttpMethod.Get, ApiRoot + "profile", access, null, ct);
        var account = Required(identity, "sub");
        var email = Required(profile, "emailAddress");
        var refresh = Text(tokens, "refresh_token");
        if (string.IsNullOrEmpty(refresh))
        {
            var previous = await repository.GetAsync(owner, ct);
            if (previous?.AccountId == account) refresh = previous.RefreshToken;
        }
        if (string.IsNullOrEmpty(refresh)) throw new MailboxException(409, "Google did not return offline access. Remove the app grant in Google and reconnect.");
        return new(owner, account, email, refresh, scope, Guid.NewGuid());
    }

    public async Task<bool> RevokeAsync(MailboxConnection connection, CancellationToken ct)
    {
        cache.Remove(connection.Version);
        try
        {
            await api.RequestAsync(HttpMethod.Post, "https://oauth2.googleapis.com/revoke", null,
                new Dictionary<string, string> { ["token"] = connection.RefreshToken }, ct);
            return true;
        }
        catch (Exception ex) when (ex is MailboxException or HttpRequestException or TaskCanceledException) { return false; }
    }

    private async Task<string> AccessTokenAsync(MailboxConnection connection, CancellationToken ct)
    {
        if (connection.ReconnectRequired) throw new MailboxException(409, "Reconnect Gmail to continue.");
        if (cache.TryGetValue<string>(connection.Version, out var token) && token is not null) return token;
        try
        {
            var response = await TokensAsync(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = connection.RefreshToken }, ct);
            token = Required(response, "access_token");
            var seconds = response["expires_in"]?.GetValue<int>() ?? 3600;
            cache.Set(connection.Version, token, TimeSpan.FromSeconds(Math.Clamp(seconds - 60, 1, 3600)));
            return token;
        }
        catch (MailboxException ex) when (ex.Status == 409)
        {
            await repository.MarkReconnectAsync(connection, ct);
            throw;
        }
    }

    private async Task<JsonObject> MailAsync(MailboxConnection connection, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        try { return await api.RequestAsync(method, ApiRoot + path, await AccessTokenAsync(connection, ct), body, ct); }
        catch (MailboxException ex) when (ex.Status == 409)
        {
            cache.Remove(connection.Version);
            await repository.MarkReconnectAsync(connection, ct);
            throw;
        }
    }

    internal static string Text(JsonNode? node, string key) => node?[key]?.GetValue<string>() ?? "";
    private static string Required(JsonNode node, string key) => !string.IsNullOrEmpty(Text(node, key)) ? Text(node, key)
        : throw new MailboxException(502, "Google returned an incomplete response.");
}
