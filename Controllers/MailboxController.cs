using System.Security.Claims;
using AiEmailer.Api.Models;
using AiEmailer.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace AiEmailer.Api.Controllers;

[ApiController, Route("api/mailbox"), Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[TypeFilter(typeof(MailboxExceptionFilter))]
public sealed partial class MailboxController(MailboxService mailbox) : ControllerBase
{
    private const string CorrelationCookie = "AiEmailer.Mailbox";
    private MailboxOwner Owner => new(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!),
        int.Parse(User.FindFirstValue("org_id")!));
    private CookieOptions Cookie => new() { HttpOnly = true, Secure = Request.IsHttps, SameSite = Request.IsHttps ? SameSiteMode.None : SameSiteMode.Lax,
        Path = "/api/mailbox/gmail/callback", MaxAge = TimeSpan.FromMinutes(10), IsEssential = true };

    [HttpGet("status"), MenuPermission("view", "inbox")]
    public async Task<IActionResult> Status(CancellationToken ct) => Ok(await mailbox.StatusAsync(Owner, ct));

    [HttpPost("connect"), MenuPermission("view", "inbox")]
    public async Task<IActionResult> Connect(CancellationToken ct)
    {
        if (Uri.TryCreate(Request.Headers.Origin.ToString(), UriKind.Absolute, out var origin) &&
            origin.IsLoopback && Request.Host.Host is "localhost" or "127.0.0.1" &&
            !string.Equals(origin.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase))
            throw new MailboxException(400, "Open the frontend at http://localhost:5500 when using the localhost API, then connect Gmail again. Mixing 127.0.0.1 and localhost can block the connection cookie.");
        var nonce = MailboxService.RandomCode();
        var url = await mailbox.ConnectAsync(Owner, Guid.Parse(User.FindFirstValue("jti")!), nonce, ct);
        var state = QueryHelpers.ParseQuery(new Uri(url).Query)["state"].ToString();
        Response.Cookies.Append(CorrelationCookie + "." + state, nonce, Cookie);
        return Ok(new { authorizationUrl = url });
    }

    [AllowAnonymous, HttpGet("gmail/callback")]
    public async Task<IActionResult> Callback(string? state, string? code, string? error, CancellationToken ct)
    {
        var cookieName = CorrelationCookie + "." + state;
        var nonce = Request.Cookies[cookieName];
        if (nonce is not null) Response.Cookies.Delete(cookieName, Cookie);
        await mailbox.CompleteAsync(state, nonce, code, error, ct);
        return Content("Gmail connected. Return to AiEmailer and refresh your inbox.", "text/plain");
    }

    [HttpDelete("connection"), MenuPermission("view", "inbox")]
    public async Task<IActionResult> Disconnect(CancellationToken ct) => Ok(new
    {
        disconnected = true, providerRevoked = await mailbox.DisconnectAsync(Owner, ct)
    });
}
