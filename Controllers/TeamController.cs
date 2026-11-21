using AiEmailer.Api.Models;
using AiEmailer.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Claims;

namespace AiEmailer.Api.Controllers;

[ApiController]
[Route("api/team")]
[Authorize]
public sealed class TeamController : ControllerBase
{
    private readonly ITeamService _service;

    public TeamController(ITeamService service) => _service = service;

    private string CurrentUuqid =>
        User.FindFirstValue("uuqid")
        ?? throw new UnauthorizedAccessException("User session UUID was not found.");

    [HttpGet("list")]
    [MenuPermission("view", "settings.team")]
    public async Task<ActionResult<List<TeamListItem>>> List(
        [FromQuery] int? idTeam = null,
        [FromQuery] int? idDept = null)
        => Ok(await _service.ListAsync(CurrentUuqid, idTeam, idDept));

    [HttpPost("addupd")]
    [MenuPermission("update", "settings.team", IdProperty = "IdTeam")]
    public async Task<IActionResult> AddUpdate([FromBody] TeamSaveRequest request)
    {
        try
        {
            await _service.AddUpdateAsync(request, CurrentUuqid);
            return Ok(new { message = "Team saved." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (SqlException ex) when (ex.Number >= 50000)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{idTeam:int}/members")]
    [MenuPermission("view", "settings.team")]
    public async Task<ActionResult<List<TeamMemberListItem>>> ListMembers(int idTeam)
    {
        try
        {
            return Ok(await _service.ListMembersAsync(idTeam, CurrentUuqid));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (SqlException ex) when (ex.Number >= 50000)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{idTeam:int}/members")]
    [MenuPermission("update", "settings.team")]
    public async Task<IActionResult> SyncMembers(int idTeam, [FromBody] TeamMemberSyncRequest request)
    {
        if (idTeam != request.IdTeam)
            return BadRequest(new { message = "Team id mismatch." });

        try
        {
            await _service.SyncMembersAsync(request, CurrentUuqid);
            return Ok(new { message = "Team members saved." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (SqlException ex) when (ex.Number >= 50000)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
