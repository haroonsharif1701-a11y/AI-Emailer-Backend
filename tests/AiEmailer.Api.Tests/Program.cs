using AiEmailer.Api.Services;
using AiEmailer.Api.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using AiEmailer.Api.Models;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.WebUtilities;

if (args.Contains("--check-database")) { await DatabaseCheck.RunAsync(); return; }
var passed = 0;
void Check(string name, bool result)
{
    if (!result) throw new Exception(name);
    Console.WriteLine("PASS " + name);
    passed++;
}
var gmail = new MailProviderSettings { Gmail = true };
gmail.Validate();
Check("Gmail allows Google login only", gmail.AllowsLogin("Google") && !gmail.AllowsLogin("EntraID"));
Check("Disabled providers deny login", !new MailProviderSettings().AllowsLogin("Google"));
try { new MailProviderSettings { Gmail = true, Outlook = true }.Validate(); throw new Exception("Accepted two providers"); }
catch (InvalidOperationException) { Check("Two providers rejected", true); }
foreach (var path in new[] { "/signin-google", "/signin-oidc", "/api/mailbox/messages" })
{
    var context = new DefaultHttpContext();
    context.Request.Path = path;
    context.Response.Body = new MemoryStream();
    var reached = false;
    await new MailProviderMiddleware(_ => { reached = true; return Task.CompletedTask; }).InvokeAsync(context, new());
    Check("Disabled route blocked: " + path, !reached && context.Response.StatusCode == 403);
}
var protector = new MailboxTokenProtector(new EphemeralDataProtectionProvider());
var owner = new MailboxOwner(1, 1);
var ciphertext = protector.Protect(owner, "test-token");
Check("Refresh credentials encrypted and recoverable", ciphertext != "test-token" && protector.Unprotect(owner, ciphertext) == "test-token");
try { protector.Unprotect(new(2, 1), ciphertext); throw new Exception("Cross-user token accepted"); }
catch (CryptographicException) { Check("Cross-user token decryption denied", true); }
try { protector.Unprotect(new(1, 2), ciphertext); throw new Exception("Cross-org token accepted"); }
catch (CryptographicException) { Check("Cross-org token decryption denied", true); }
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Authentication:Google:ClientId"] = "test-client", ["Authentication:Google:ClientSecret"] = "test-secret",
    ["Mailbox:Gmail:RedirectUri"] = "https://localhost:5080/api/mailbox/gmail/callback"
}).Build();
var repository = new FakeRepository();
var handler = new FakeHttp();
using var http = new HttpClient(handler);
using var cache = new MemoryCache(new MemoryCacheOptions());
var provider = new GmailMailProvider(new GmailApiClient(http), config, repository, cache);
var sessions = new FakeSessions();
var service = new MailboxService(repository, provider, sessions, gmail);
async Task Fails(string name, Func<Task> operation, int status)
{
    try { await operation(); throw new Exception(name + " was accepted"); }
    catch (MailboxException ex) { Check(name, ex.Status == status); }
}
var nonce = MailboxService.RandomCode();
var url = await service.ConnectAsync(owner, Guid.NewGuid(), nonce, default);
var query = QueryHelpers.ParseQuery(new Uri(url).Query);
Check("Consent requests offline Gmail and PKCE", query["access_type"] == "offline" && query["code_challenge_method"] == "S256" && query["scope"].ToString().Contains(GmailMailProvider.MailScope));
Check("Authorization URL contains no secret", !url.Contains("test-secret"));
var state = query["state"].ToString();
await Fails("Wrong browser denied", () => service.CompleteAsync(state, MailboxService.RandomCode(), "code", null, default), 400);
handler.Respond = _ => Task.FromResult(FakeHttp.Json("""{"error":"invalid_grant"}""", System.Net.HttpStatusCode.BadRequest));
await Fails("Expired Google authorization rejected", () => service.CompleteAsync(state, nonce, "code", null, default), 409);
await Fails("OAuth callback replay denied", () => service.CompleteAsync(state, nonce, "code", null, default), 400);
url = await service.ConnectAsync(owner, Guid.NewGuid(), nonce, default);
state = QueryHelpers.ParseQuery(new Uri(url).Query)["state"].ToString();
sessions.Active = false;
await Fails("Revoked login blocks callback", () => service.CompleteAsync(state, nonce, "code", null, default), 403);
sessions.Active = true;
url = await service.ConnectAsync(owner, Guid.NewGuid(), nonce, default);
state = QueryHelpers.ParseQuery(new Uri(url).Query)["state"].ToString();
repository.CanRead = false;
await Fails("Revoked permission blocks callback", () => service.CompleteAsync(state, nonce, "code", null, default), 403);
repository.CanRead = true;
handler.Respond = request => Task.FromResult(FakeHttp.Json(request.RequestUri!.AbsolutePath switch
{
    "/token" => """{"access_token":"access","refresh_token":"refresh","scope":"https://www.googleapis.com/auth/gmail.modify"}""",
    "/v1/userinfo" => """{"sub":"google-account","email_verified":true}""",
    _ => """{"emailAddress":"test@example.com","historyId":"100"}"""
}));
url = await service.ConnectAsync(owner, Guid.NewGuid(), nonce, default);
state = QueryHelpers.ParseQuery(new Uri(url).Query)["state"].ToString();
await service.CompleteAsync(state, nonce, "code", null, default);
Check("Successful consent links only initiating owner", repository.Connections[owner].AccountId == "google-account" && !repository.Connections.ContainsKey(new(2, 1)));
handler.Respond = request => Task.FromResult(FakeHttp.Json(request.RequestUri!.AbsolutePath == "/token"
    ? """{"access_token":"access","expires_in":3600}""" : request.RequestUri.AbsolutePath.EndsWith("/messages")
    ? """{"messages":[{"id":"abc"}],"nextPageToken":"next-page"}"""
    : """{"id":"abc","threadId":"thread","internalDate":"0","labelIds":["INBOX","UNREAD"],"payload":{"mimeType":"multipart/mixed","headers":[{"name":"Subject","value":"Hello"}],"parts":[{"partId":"0","mimeType":"text/plain","body":{"data":"SGVsbG8"}},{"partId":"1","filename":"sample.pdf","mimeType":"application/pdf","body":{"size":10485761,"attachmentId":"large"}}]}}"""));
var page = await service.ListAsync(owner, 10, null, null, default);
Check("Inbox pagination preserves cursor", page.Messages.Count == 1 && page.NextPageToken == "next-page");
var message = await service.GetAsync(owner, "abc", default);
Check("MIME text and attachment metadata decoded", message.BodyText == "Hello" && message.Attachments.Length == 1 && message.Labels.Contains("UNREAD"));
await Fails("Oversized attachments rejected before download", () => service.AttachmentAsync(owner, "abc", "1", default), 413);
await Fails("Unknown attachment rejected", () => service.AttachmentAsync(owner, "abc", "other", default), 404);
await Fails("Page size bounded", () => service.ListAsync(owner, 100, null, null, default), 400);
await Fails("Other user cannot read connected mailbox", () => service.GetAsync(new(2, 1), "abc", default), 409);
var disabled = new MailboxService(repository, provider, sessions, new());
await Fails("Existing connection cannot bypass provider flag", () => disabled.GetAsync(owner, "abc", default), 403);
var llm = new FakeLlm { Result = """{"summary":"Summary","actionItems":[],"priority":"Normal"}""" };
var ai = new MailboxAiService(service, llm, new AttachmentTextExtractor());
Check("Mailbox summary uses existing contract", (await ai.SummarizeAsync(owner, "abc", default)).Summary == "Summary" && llm.Input.Contains("Hello"));
llm.Result = "{}";
foreach (var newline in new[] { "\n", "\r\n" })
{
    llm.Result = "```json" + newline + "{\"summary\":\"Summary\",\"actionItems\":[],\"priority\":\"Normal\"}" + newline + "```";
    Check("Markdown summary parsed with newline length " + newline.Length, (await ai.SummarizeAsync(owner, "abc", default)).Summary == "Summary");
}
llm.Result = "{}";
await Fails("Incomplete AI JSON returns 502", () => ai.SummarizeAsync(owner, "abc", default), 502);
llm.Result = "not json";
await Fails("Malformed AI JSON returns 502", () => ai.SummarizeAsync(owner, "abc", default), 502);
llm.Result = "Draft for review";
Check("Reply generation does not send mail", (await ai.ReplyAsync(owner, "abc", default)).Draft == "Draft for review");
await Fails("Recipient header injection rejected", () => service.SendAsync(owner, new("victim@example.com\r\nBcc:other@example.com", "Subject", "Body"), default), 400);
await Fails("Subject header injection rejected", () => service.SendAsync(owner, new("test@example.com", "Subject\r\nBcc:x@y.com", "Body"), default), 400);
string? sentMime = null;
handler.Respond = async request =>
{
    var json = System.Text.Json.Nodes.JsonNode.Parse(await request.Content!.ReadAsStringAsync());
    sentMime = System.Text.Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(json!["raw"]!.GetValue<string>()));
    return FakeHttp.Json("""{"id":"sent"}""");
};
Check("Explicit send creates MIME message", await service.SendAsync(owner, new("test@example.com", "Subject", "Body"), default) == "sent" && sentMime!.Contains("Content-Transfer-Encoding: base64"));
var callsBefore = handler.Calls;
handler.Respond = _ => Task.FromResult(FakeHttp.Json("{}", System.Net.HttpStatusCode.ServiceUnavailable));
await Fails("Send failure reported without automatic retry", () => service.SendAsync(owner, new("test@example.com", "Subject", "Body"), default), 502);
Check("Send attempted once", handler.Calls == callsBefore + 1);
string? actionBody = null;
handler.Respond = async request => { actionBody = await request.Content!.ReadAsStringAsync(); return FakeHttp.Json("{}"); };
await service.ModifyAsync(owner, "abc", "archive", default);
Check("Archive removes INBOX label", actionBody!.Contains("INBOX") && actionBody.Contains("removeLabelIds"));
await Fails("Unknown mailbox action rejected", () => service.ModifyAsync(owner, "abc", "permanent-delete", default), 400);
handler.Respond = _ => Task.FromResult(FakeHttp.Json("""{"historyId":"100"}"""));
Check("Initial sync establishes live-mailbox cursor", (await service.SyncAsync(owner, default)).Reset && repository.Connections[owner].HistoryId == "100");
handler.Respond = request => Task.FromResult(FakeHttp.Json(request.RequestUri!.Query.Contains("pageToken=next")
    ? """{"historyId":"102","history":[{"id":"102"}]}""" : """{"historyId":"102","history":[{"id":"101"}],"nextPageToken":"next"}"""));
var sync = await service.SyncAsync(owner, default);
Check("History pagination completes before saving cursor", !sync.Reset && sync.Changes == 2 && repository.Connections[owner].HistoryId == "102");
handler.Respond = request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/history")
    ? FakeHttp.Json("{}", System.Net.HttpStatusCode.NotFound) : FakeHttp.Json("""{"historyId":"200"}"""));
Check("Expired history recovers by resetting live view", (await service.SyncAsync(owner, default)).Reset && repository.Connections[owner].HistoryId == "200");
repository.CanRead = false;
await Fails("Background sync respects permission revocation", () => service.SyncAsync(owner, default), 403);
repository.CanRead = true;
cache.Remove(repository.Connections[owner].Version);
handler.Respond = _ => Task.FromResult(FakeHttp.Json("""{"error":"invalid_grant"}""", System.Net.HttpStatusCode.BadRequest));
await Fails("Revoked refresh token requests reconnection", () => service.SyncAsync(owner, default), 409);
Check("Revoked token marks connection for reconnect", repository.Connections[owner].ReconnectRequired);
repository.Connections[owner] = repository.Connections[owner] with { ReconnectRequired = false };
cache.Set(repository.Connections[owner].Version, "test-access");
handler.Respond = request => Task.FromResult(request.Method == HttpMethod.Get
    ? FakeHttp.Json("""{"id":"abc","threadId":"thread-1","internalDate":"0","payload":{"headers":[{"name":"Subject","value":"Original subject"},{"name":"Message-ID","value":"<original@example.com>"}]}}""")
    : FakeHttp.Json("""{"id":"reply"}"""));
string? replyThread = null;
var responder = handler.Respond;
handler.Respond = async request =>
{
    if (request.Method == HttpMethod.Post)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(await request.Content!.ReadAsStringAsync());
        replyThread = json!["threadId"]!.GetValue<string>();
        sentMime = System.Text.Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(json["raw"]!.GetValue<string>()));
    }
    return await responder(request);
};
await service.SendAsync(owner, new("test@example.com", "Changed subject", "Reply", "abc"), default);
Check("Replies preserve provider thread and RFC headers", replyThread == "thread-1" && sentMime!.Contains("In-Reply-To: <original@example.com>") && sentMime.Contains("References: <original@example.com>"));
callsBefore = handler.Calls;
handler.Respond = _ =>
{
    var response = FakeHttp.Json("{}", System.Net.HttpStatusCode.TooManyRequests);
    response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
    return Task.FromResult(response);
};
await Fails("Read rate limits return 429 after bounded retries", () => service.GetAsync(owner, "abc", default), 429);
Check("Read retries are bounded to three attempts", handler.Calls == callsBefore + 3);
var priorHistory = repository.Connections[owner].HistoryId;
handler.Respond = request => Task.FromResult(request.RequestUri!.Query.Contains("pageToken=next")
    ? FakeHttp.Json("{}", System.Net.HttpStatusCode.Forbidden)
    : FakeHttp.Json("""{"historyId":"999","nextPageToken":"next"}"""));
await Fails("Failed sync page returns provider error", () => service.SyncAsync(owner, default), 403);
Check("Partial sync does not advance saved cursor", repository.Connections[owner].HistoryId == priorHistory);
handler.Respond = _ => Task.FromResult(FakeHttp.Json("{}", System.Net.HttpStatusCode.ServiceUnavailable));
Check("Disconnect removes local credentials even if revocation fails", !await service.DisconnectAsync(owner, default) && !repository.Connections.ContainsKey(owner));
Check("Disconnected status exposes no credentials", !(await service.StatusAsync(owner, default)).Connected);
var controllerType = typeof(AiEmailer.Api.Controllers.MailboxController);
Check("Mailbox controller requires authorization", controllerType.IsDefined(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true));
var publicActions = controllerType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
    .Where(method => method.GetCustomAttributes(true).Any(attribute => attribute is Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute));
Check("Only mailbox OAuth callback is anonymous", publicActions.Where(method => method.IsDefined(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), true)).Select(method => method.Name).SequenceEqual(new[] { "Callback" }));
Check("All authenticated mailbox actions have RBAC", publicActions.Where(method => method.Name != "Callback").All(method => method.IsDefined(typeof(MenuPermissionAttribute), true)));
Check("AI actions require inbox and feature permissions", controllerType.GetMethod("ReplyDraft")!.GetCustomAttributes(typeof(MenuPermissionAttribute), true).Length == 2 && controllerType.GetMethod("Analysis")!.GetCustomAttributes(typeof(MenuPermissionAttribute), true).Length == 2);
var sendParameters = typeof(SendMailRequest).GetConstructors()[0].GetParameters();
Check("Send DTO declares recipient and body validation", sendParameters.First(parameter => parameter.Name == "To")
    .IsDefined(typeof(System.ComponentModel.DataAnnotations.EmailAddressAttribute), true) && sendParameters.First(parameter => parameter.Name == "Body")
    .IsDefined(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute), true));
var jsonHandler = new FakeHttp();
string? requestFormat = null;
jsonHandler.Respond = async request =>
{
    var payload = System.Text.Json.Nodes.JsonNode.Parse(await request.Content!.ReadAsStringAsync());
    requestFormat = payload!["format"]?.GetValue<string>();
    return FakeHttp.Json("{\"message\":{\"content\":\"{}\"}}");
};
using var jsonHttp = new HttpClient(jsonHandler);
ILlmProvider jsonProvider = new OllamaLlmProvider(jsonHttp, config, Microsoft.Extensions.Logging.Abstractions.NullLogger<OllamaLlmProvider>.Instance);
await jsonProvider.GenerateJsonAsync("Return JSON", "Test");
Check("Ollama structured generation requests JSON format", requestFormat == "json");
await jsonProvider.GenerateAsync("Return text", "Test");
Check("Ollama replies remain plain text", requestFormat is null);
await MailboxRegressionChecks.RunAsync(Check, service, provider, repository, handler, owner);
Console.WriteLine($"{passed} checks passed.");
