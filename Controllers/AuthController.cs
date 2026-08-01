using AiEmailer.Api.Models;
using AiEmailer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AiEmailer.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IJwtTokenService _jwt;

    public AuthController(IJwtTokenService jwt)
    {
        _jwt = jwt;
    }

    // NOTE: this is a placeholder credential check so the frontend you
    // already built has something real to call. Replace the body of this
    // method with ASP.NET Core Identity + a Users table (per Module 1 /
    // Module 3 in the plan) before this goes anywhere near production —
    // right now it accepts exactly one hardcoded account and does not
    // hash/compare a real password store.
    [HttpPost("login")]
    public ActionResult<LoginResponse> Login([FromBody] LoginRequest request)
    {
        const string demoEmail = "admin@company.com";
        const string demoPassword = "Password123!";

        if (request.Email != demoEmail || request.Password != demoPassword)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        var token = _jwt.CreateToken(userId: "1", email: demoEmail, role: "Administrator");
        return Ok(new LoginResponse(token, demoEmail, "Administrator", 30));
    }
}
