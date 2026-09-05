using AiEmailer.Api.Models;
using AiEmailer.Api.Services;
using DocumentFormat.OpenXml.Office2016.Drawing.ChartDrawing;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualBasic;

namespace AiEmailer.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IJwtTokenService _jwt;
    private readonly IAuthService _authService;

    public AuthController(IJwtTokenService jwt, IAuthService authService)
    {
        _jwt = jwt;
        _authService = authService;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        var result = await _authService.LoginAsync(request.Email, request.Password);

        if (result.Status != LoginStatus.Success)
        {
            return Ok(new LoginResponse((int)result.Status, null, null, null, null, null, null, null, 0, null));
        }

        var token = _jwt.CreateToken(
            userId: result.IdUser!.Value.ToString(),
            email: result.Username!,
            role: result.Role ?? "Unknown",
            idOrg: result.IdOrg!.Value.ToString(),
            uuqid: result.Uuqid.ToString()
        );

        HttpContext.Session.SetString("Uuqid", result.Uuqid.ToString());
        HttpContext.Session.SetInt32("IdOrg", result.IdOrg ?? 0);
        HttpContext.Session.SetString("Email", result.Username);

        DateTime LogTime = DateTime.Now ;

        return Ok(new LoginResponse(
            (int)result.Status,
            token,
            result.Username,
            result.FullName,
            result.Uuqid,
            result.IdRole,
            result.Role,
            result.IdOrg,
            30,
            LogTime
        ));
    }

    [HttpPost("logout")]

    public async Task<ActionResult> Logout() 
    {

        string uuqid = HttpContext.Session.GetString("Uuqid");
        HttpContext.Session.Remove("Uuqid");

        return Ok("Logout Successful!");
    }
}
