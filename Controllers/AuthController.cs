using System.Security.Claims;
using System.Text.RegularExpressions;
using AiEmailer.Api.Models;
using AiEmailer.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace AiEmailer.Api.Controllers;

[ApiController]
[Route("api/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AuthController(IJwtTokenService jwt, IAuthService authService,
    IExternalAuthService externalAuth, IConfiguration config, ILoginSessionService sessions, MailProviderSettings providers) : ControllerBase
{
    [HttpGet("providers")]
    public IActionResult Providers() => Ok(new { gmail = providers.Gmail, outlook = providers.Outlook });

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        var result = await authService.LoginAsync(request.Email, request.Password);
        if (result.Status != LoginStatus.Success)
            return Ok(new LoginResponse((int)result.Status,null,null,null,null,null,null,null,0,null));
        return await IssueTokenAsync(result);
    }

    [HttpGet("login-external")]
    public IActionResult LoginExternal([FromQuery] string provider, [FromQuery] string challenge)
    {
        if (provider is not ("Google" or "EntraID") || !IsCode(challenge))
            return BadRequest(new { message = "A supported provider and browser challenge are required." });
        if (!providers.AllowsLogin(provider)) return StatusCode(403, new { message = "This provider is disabled." });
        var properties = new AuthenticationProperties { RedirectUri = "/api/auth/external-callback" };
        properties.Items["app_challenge"] = challenge;
        properties.Items["app_provider"] = provider;
        return Challenge(properties, provider);
    }

    [HttpGet("external-callback")]
    public async Task<ActionResult> ExternalCallback()
    {
        var result = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        // The provider stores these values in its protected authentication state/cookie.
        string? challenge = null, provider = null;
        result.Properties?.Items.TryGetValue("app_challenge", out challenge);
        result.Properties?.Items.TryGetValue("app_provider", out provider);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!providers.AllowsLogin(provider)) return StatusCode(403, new { message = "This provider is disabled." });
        if (!result.Succeeded || result.Principal is null || !IsCode(challenge) || provider is not ("Google" or "EntraID"))
            return Unauthorized(new { message = "External login could not be verified. Start login again." });
        var login = await externalAuth.ProcessExternalLoginAsync(result.Principal, provider);
        if (login.Status != LoginStatus.Success)
            return Redirect(QueryHelpers.AddQueryString(CallbackUrl(), "error", "unauthorized_organization"));
        var code = await sessions.CreateHandoffAsync(login.IdUser!.Value, login.IdOrg!.Value, challenge!, HttpContext.RequestAborted);
        return Redirect(QueryHelpers.AddQueryString(CallbackUrl(), "code", code));
    }

    [HttpPost("exchange")]
    public async Task<ActionResult<LoginResponse>> Exchange([FromBody] OAuthExchangeRequest request)
    {
        if (!providers.Gmail && !providers.Outlook) return StatusCode(403, new { message = "External login is disabled." });
        if (!IsCode(request.Code) || !IsCode(request.Verifier))
            return BadRequest(new { message = "Invalid login handoff." });
        var login = await sessions.ExchangeAsync(request.Code, request.Verifier, HttpContext.RequestAborted);
        if (login is null) return BadRequest(new { message = "Login link expired, was already used, or belongs to another browser. Sign in again." });
        return await IssueTokenAsync(login);
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<ActionResult> Logout()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
        await sessions.RevokeAsync(Guid.Parse(User.FindFirstValue("jti")!), userId, HttpContext.RequestAborted);
        HttpContext.Session.Clear();
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { message = "Signed out." });
    }

    private async Task<ActionResult<LoginResponse>> IssueTokenAsync(LoginResult login)
    {
        var sessionId = await sessions.CreateAsync(login.IdUser!.Value, login.IdOrg!.Value, HttpContext.RequestAborted);
        var token = jwt.CreateToken(login.IdUser.Value.ToString(), login.Username!, login.Role ?? "Unknown",
            login.IdOrg.Value.ToString(), login.Uuqid!.Value.ToString(), sessionId);
        return Ok(new LoginResponse((int)login.Status,token,login.Username,login.FullName,login.Uuqid,
            login.IdRole,login.Role,login.IdOrg,LoginSessionService.LifetimeMinutes,DateTime.UtcNow));
    }

    private string CallbackUrl()
    {
        var value = ApplicationUrls.LoginCallback(config);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Configure a frontend OAuth callback URL without query parameters or fragment.");
        return value;
    }

    private static bool IsCode(string? value) => value is not null && Regex.IsMatch(value, "^[A-Za-z0-9_-]{43}$");
}

public record OAuthExchangeRequest(string Code, string Verifier);
