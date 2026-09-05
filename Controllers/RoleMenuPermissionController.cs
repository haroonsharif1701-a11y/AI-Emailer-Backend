using AiEmailer.Api.Models;
using AiEmailer.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AiEmailer.Api.Controllers
{
    [ApiController]
    [Route("api/permission")]
    [Authorize]
    public class RoleMenuPermissionController : ControllerBase
    {
        private readonly IRoleMenuPermissionService _permissionService;
        public RoleMenuPermissionController(IRoleMenuPermissionService permissionService)
        {
            _permissionService = permissionService;
        }

        [HttpGet("list")]
        public async Task<ActionResult<List<RoleMenuPermissionItem>>> List([FromQuery] int idRole)
        {
            if (idRole <= 0) return BadRequest("A valid role id is required.");
            var permissions = await _permissionService.ListAsync(idRole);
            return Ok(permissions);
        }

        [HttpPost("save")]
        public async Task<ActionResult> Save([FromBody] RoleMenuPermissionSaveRequest request)
        {
            if (request.IdRole <= 0) return BadRequest("A valid role id is required.");

            var userId = GetCurrentUserId();
            await _permissionService.SaveAsync(request, userId);
            return Ok();
        }

        private int GetCurrentUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? throw new InvalidOperationException("User id claim not found.");
            return int.Parse(claim);
        }
    }
}
