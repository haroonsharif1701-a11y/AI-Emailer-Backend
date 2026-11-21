using AiEmailer.Api.Models;
using AiEmailer.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using static AiEmailer.Api.Models.RoleModel;

namespace AiEmailer.Api.Controllers;

[ApiController]
[Route("api/role")]
[Authorize]
public class RoleController : ControllerBase
{
    private readonly IRoleService _roleService;

    public RoleController(IRoleService roleService)
    {
        _roleService = roleService;
    }

    [HttpGet("list")]
    [MenuPermission("view", "settings.roles", "settings.permissions", "settings.users")]
    public async Task<ActionResult<List<RoleListItem>>> List()
    {
        var roles = await _roleService.ListAsync();
        return Ok(roles);
    }

    [HttpPost("addupd")]
    [MenuPermission("update", "settings.roles", IdProperty = "IdRole")]
    public async Task<IActionResult> AddUpdate([FromBody] RoleSaveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RoleName))
            return BadRequest(new { message = "Role name is required." });

        await _roleService.AddUpdateAsync(request, GetCurrentUserId());
        return Ok();
    }

    // ---- NEW ----
    [HttpPost("delete")]
    [MenuPermission("delete", "settings.roles")]
    public async Task<IActionResult> Delete([FromBody] RoleDeleteRequest request)
    {
        var idRole = request.IdRole;
        if (idRole <= 0)
            return BadRequest(new { message = "A valid role id is required." });

        await _roleService.DeleteAsync(idRole);
        return Ok();
    }

    // See note at top of file re: matching your project's actual claim/helper.
    private int GetCurrentUserId()
    {
        var raw = User.FindFirstValue("idUser")
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub");
        return int.TryParse(raw, out var id) ? id : 0;
    }
}

public record RoleDeleteRequest(int IdRole);
