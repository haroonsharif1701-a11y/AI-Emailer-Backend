using System.Net;
using System.Text;
using AiEmailer.Api.Models;
using AiEmailer.Api.Services;

sealed class FakeRepository : IMailboxRepository
{
    public Dictionary<MailboxOwner, MailboxConnection> Connections { get; } = new();
    public Dictionary<string, (string Nonce, MailboxAuthorization Authorization)> Pending { get; } = new();
    public bool CanRead { get; set; } = true;
    public Task<MailboxConnection?> GetAsync(MailboxOwner owner, CancellationToken ct) => Task.FromResult(Connections.GetValueOrDefault(owner));
    public Task SaveAsync(MailboxConnection connection, CancellationToken ct) { Connections[connection.Owner] = connection; return Task.CompletedTask; }
    public Task<MailboxConnection?> DeleteAsync(MailboxOwner owner, CancellationToken ct) => Task.FromResult(Connections.Remove(owner, out var value) ? value : null);
    public Task MarkReconnectAsync(MailboxConnection connection, CancellationToken ct) => SaveAsync(connection with { ReconnectRequired = true }, ct);
    public Task SaveSyncAsync(MailboxConnection connection, string historyId, CancellationToken ct) => SaveAsync(connection with { HistoryId = historyId }, ct);
    public Task<IReadOnlyList<MailboxOwner>> ListOwnersAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<MailboxOwner>>(Connections.Keys.ToList());
    public Task<bool> CanReadAsync(MailboxOwner owner, CancellationToken ct) => Task.FromResult(CanRead);
    public Task CreateAuthorizationAsync(string state, string browserNonce, MailboxAuthorization authorization, CancellationToken ct)
    { Pending[state] = (browserNonce, authorization); return Task.CompletedTask; }
    public Task<MailboxAuthorization?> ConsumeAuthorizationAsync(string state, string browserNonce, CancellationToken ct)
    {
        if (!Pending.TryGetValue(state, out var pending) || pending.Nonce != browserNonce) return Task.FromResult<MailboxAuthorization?>(null);
        Pending.Remove(state);
        return Task.FromResult<MailboxAuthorization?>(pending.Authorization);
    }
}

sealed class FakeSessions : ILoginSessionService
{
    public bool Active { get; set; } = true;
    public Task<bool> IsActiveAsync(Guid sessionId, int userId, int orgId, CancellationToken ct) => Task.FromResult(Active);
    public Task<Guid> CreateAsync(int userId, int orgId, CancellationToken ct) => throw new NotSupportedException();
    public Task RevokeAsync(Guid sessionId, int userId, CancellationToken ct) => throw new NotSupportedException();
    public Task<string> CreateHandoffAsync(int userId, int orgId, string challenge, CancellationToken ct) => throw new NotSupportedException();
    public Task<LoginResult?> ExchangeAsync(string code, string verifier, CancellationToken ct) => throw new NotSupportedException();
}

sealed class FakeHttp : HttpMessageHandler
{
    public Func<HttpRequestMessage, Task<HttpResponseMessage>> Respond { get; set; } = _ => Task.FromResult(Json("{}"));
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    { Calls++; return Respond(request); }
    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

sealed class FakeLlm : ILlmProvider
{
    public string Result { get; set; } = "{}";
    public string Input { get; private set; } = "";
    public Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    { Input = userPrompt; return Task.FromResult(Result); }
}
