using System.Security.Claims;
using AiEmailer.Api.Models;
using AiEmailer.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiEmailer.Api.Controllers;

[ApiController]
[Route("api/user")]
[Authorize]
public class UserController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ILogger<UserController> _logger;

    public UserController(IUserService userService, ILogger<UserController> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    // Reads the acting user's own id/org straight from the JWT claims —
    // never trusts an org/user id passed in from the client for these,
    // since that would let one org's admin read or edit another org's users.
    //
    // Checks both ClaimTypes.NameIdentifier and the raw "sub" claim name:
    // ASP.NET Core maps "sub" -> ClaimTypes.NameIdentifier by default, but
    // that mapping depends on JwtBearerOptions.MapInboundClaims and has
    // changed behavior across .NET versions — checking both avoids this
    // silently breaking on a future upgrade.
    private int CurrentUserId => int.Parse(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")
        ?? throw new InvalidOperationException("Token is missing a user id claim."));

    private string CurrentUUqid =>
        User.FindFirstValue("uuqid")
        ?? throw new InvalidOperationException("User UUID is missing from token.");
    private int CurrentOrgId =>
        int.TryParse(User.FindFirst("org_id")?.Value, out var claimOrgId)
            ? claimOrgId
            : HttpContext.Session.GetInt32("IdOrg")
                ?? throw new InvalidOperationException(
                    "Organization ID is missing from both JWT claims and session.");

    [HttpGet("list")]
    [MenuPermission("view", "settings.users", "organization", "settings.team")]
    public async Task<ActionResult<List<UserListItem>>> List()
    {
        var users = await _userService.ListAsync(CurrentOrgId);
        return Ok(users);
    }

    [HttpGet("{id:int}")]
    [MenuPermission("view", "settings.users")]
    public async Task<ActionResult<UserDetail>> Get(int id)
    {
        string? uuqid = null; //HttpContext.Session.GetString("Uuqid");
        var user = await _userService.GetAsync(id, uuqid);
        if (user is null) return NotFound(new { message = $"User {id} not found." });

        // Extra guard beyond CurrentOrgId scoping on List: a user id from
        // another org should 404, not leak that org's user data.
        if (user.IdOrg != CurrentOrgId) return NotFound(new { message = $"User {id} not found." });

        return Ok(user);
    }

    [HttpPost("addupd")]
    [MenuPermission("update", "settings.users", IdProperty = "IdUser")]
    public async Task<IActionResult> AddUpdate([FromBody] UserSaveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.FullName))
        {
            return BadRequest(new { message = "Username and full name are required." });
        }
        string uuqid = CurrentUUqid;
        if (request.IdUser != 0)
        {
            // Confirm the user being edited actually belongs to the caller's
            // org before touching it — same reasoning as Get above.
            var existing = await _userService.GetAsync(request.IdUser, null);
            if (existing is null || existing.IdOrg != CurrentOrgId)
            {
                return NotFound(new { message = $"User {request.IdUser} not found." });
            }
        }

        try
        {
            await _userService.AddUpdateAsync(request, CurrentUserId, uuqid);
            return Ok(new { message = "Saved." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:int}")]
    [MenuPermission("delete", "settings.users")]
    public async Task<IActionResult> Delete(int id)
    {
        string uuqid = CurrentUUqid;
        // Backend-only for now — no UI button wired to this yet, per scope.
        var existing = await _userService.GetAsync(id, uuqid);
        if (existing is null || existing.IdOrg != CurrentOrgId)
        {
            return NotFound(new { message = $"User {id} not found." });
        }

        await _userService.DeleteAsync(id);
        return Ok(new { message = "Deleted." });
    }

    [HttpPut("me")]
    [MenuPermission("update", "settings.profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] ProfileUpdateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.FullName))
            return BadRequest(new { message = "Full name and email are required." });
        await _userService.UpdateProfileAsync(CurrentUserId, CurrentOrgId, request);
        return Ok(new { message = "Profile saved." });
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserDetail>> Me()
    {
        string? uuqid = CurrentUUqid;

        if (string.IsNullOrWhiteSpace(uuqid))
        {
            return Unauthorized(new { message = "User session not found." });
        }

        var user = await _userService.GetAsync(CurrentUserId, uuqid);

        if (user is null || user.IdOrg != CurrentOrgId)
        {
            return NotFound(new { message = "Logged-in user not found." });
        }

        if (!user.IsActive) return Forbid();
        return Ok(user);
    }
}
