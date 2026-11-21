using AiEmailer.Api.Controllers;
using AiEmailer.Api.Models;
using AiEmailer.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using System.Security.Claims;

static class MailboxRegressionChecks
{
    public static async Task RunAsync(Action<string, bool> check, MailboxService service, GmailMailProvider provider,
        FakeRepository repository, FakeHttp handler, MailboxOwner owner)
    {
        foreach (var aborted in new[] { true, false })
        {
            var http = new DefaultHttpContext { RequestAborted = new CancellationToken(aborted) };
            var context = new ExceptionContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), [])
            { Exception = aborted ? new OperationCanceledException() : new TaskCanceledException("timeout", new TimeoutException()) };
            new MailboxExceptionFilter().OnException(context);
            check(aborted ? "Client cancellation returns 499 without error body" : "Upstream timeout returns 504",
                context.ExceptionHandled && (aborted ? context.Result is StatusCodeResult { StatusCode: 499 }
                    : context.Result is ObjectResult { StatusCode: 504 }));
        }
        var request = new DefaultHttpContext();
        request.Request.Scheme = "https";
        request.Request.Host = new HostString("localhost", 5080);
        request.Request.Headers.Origin = "http://127.0.0.1:5500";
        request.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim("org_id", "1"), new Claim("jti", Guid.NewGuid().ToString()) }, "test"));
        var controller = new MailboxController(service) { ControllerContext = new ControllerContext { HttpContext = request } };
        try { await controller.Connect(default); throw new Exception("Mixed local hosts accepted"); }
        catch (MailboxException ex) { check("Mixed localhost and IP rejected before consent", ex.Status == 400); }
        request.Request.Headers.Origin = "http://localhost:5500";
        await controller.Connect(default);
        await controller.Connect(default);
        var cookies = request.Response.Headers.SetCookie.ToArray();
        check("Overlapping consent attempts use separate HttpOnly cookies", cookies.Length == 2 &&
            cookies[0]!.Split('=')[0] != cookies[1]!.Split('=')[0] && cookies.All(x => x!.Contains("httponly")));
        handler.Respond = message => Task.FromResult(FakeHttp.Json(message.RequestUri!.AbsolutePath switch
        {
            "/token" => "{\"access_token\":\"access\",\"refresh_token\":\"refresh\",\"scope\":\"https://www.googleapis.com/auth/gmail.modify\"}",
            "/v1/userinfo" => "{\"sub\":\"test-account\",\"email_verified\":true}",
            _ => "{\"emailAddress\":\"test@example.com\"}"
        }));
        foreach (var cookie in cookies)
        {
            var callbackRequest = new DefaultHttpContext();
            callbackRequest.Request.Scheme = "https";
            callbackRequest.Request.Headers.Cookie = cookie!.Split(';')[0];
            var callback = new MailboxController(service) { ControllerContext = new ControllerContext { HttpContext = callbackRequest } };
            var state = cookie.Split('=')[0]["AiEmailer.Mailbox.".Length..];
            check("Each overlapping callback accepts its own cookie", await callback.Callback(state, "code", null, default) is ContentResult);
        }
        try { await service.CompleteAsync(MailboxService.RandomCode(), null, "code", null, default); throw new Exception("Missing cookie accepted"); }
        catch (MailboxException ex) { check("Missing correlation cookie reports recovery steps", ex.Status == 400 && ex.Message.Contains("cookie")); }

        repository.Connections[owner] = new(owner, "test-account", "test@example.com", "refresh", GmailMailProvider.MailScope, Guid.NewGuid());
        var active = 0;
        var peak = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Respond = async message =>
        {
            if (message.RequestUri!.AbsolutePath.EndsWith("/token")) return FakeHttp.Json("{\"access_token\":\"access\",\"expires_in\":3600}");
            if (message.RequestUri.AbsolutePath.EndsWith("/messages"))
                return FakeHttp.Json("{\"messages\":[" + string.Join(',', Enumerable.Range(0, 12).Select(i => "{\"id\":\"" + i + "\"}")) + "],\"nextPageToken\":\"next\"}");
            var count = Interlocked.Increment(ref active);
            lock (gate) peak = Math.Max(peak, count);
            if (count == 5) gate.TrySetResult();
            try
            {
                await gate.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Task.Delay(10);
                return FakeHttp.Json("{\"id\":\"" + message.RequestUri.Segments.Last() + "\",\"internalDate\":\"0\"}");
            }
            finally { Interlocked.Decrement(ref active); }
        };
        var page = await provider.ListAsync(repository.Connections[owner], 25, null, null, default);
        check("Inbox uses at most five concurrent detail requests", peak == 5);
        check("Concurrent inbox preserves Gmail order and cursor", page.Messages.Select(x => x.Id).SequenceEqual(Enumerable.Range(0, 12).Select(x => x.ToString())) && page.NextPageToken == "next");
        try { await provider.ListAsync(repository.Connections[owner], 25, null, null, new CancellationToken(true)); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { check("Inbox propagates caller cancellation", true); }
    }
}
