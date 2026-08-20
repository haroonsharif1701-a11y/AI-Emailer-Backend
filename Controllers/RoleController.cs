using AiEmailer.Api.Models;
using AiEmailer.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
    public async Task<ActionResult<List<RoleListItem>>> List()
    {
        var roles = await _roleService.ListAsync();
        return Ok(roles);
    }
}
