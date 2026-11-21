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

        [HttpGet("me")]
        public async Task<ActionResult<AccessSnapshot>> Me([FromServices] ICurrentAccessService accessService)
        {
            Response.Headers.CacheControl = "no-store";
            var access = await accessService.GetAsync(HttpContext.RequestAborted);
            return access is null ? Forbid() : Ok(access);
        }

        [HttpGet("list")]
        [MenuPermission("view", "settings.permissions")]
        public async Task<ActionResult<RolePermissionSet>> List([FromQuery] int idRole)
        {
            if (idRole <= 0) return BadRequest("A valid role id is required.");
            var permissions = await _permissionService.ListAsync(idRole);
            return Ok(permissions);
        }

        [HttpPost("save")]
        [MenuPermission("update", "settings.permissions")]
        public async Task<ActionResult> Save([FromBody] RoleMenuPermissionSaveRequest request)
        {
            if (request.IdRole <= 0) return BadRequest("A valid role id is required.");

            if (request.Permissions is null || request.Permissions.Any(p => p.IdMenu <= 0 || (!p.View && (p.Add || p.Update || p.Delete))) ||
                request.Permissions.Select(p => p.IdMenu).Distinct().Count() != request.Permissions.Count)
                return BadRequest(new { message = "Permissions must have unique menu ids and View before other actions." });
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
