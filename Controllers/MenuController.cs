using AiEmailer.Api.Models;
using AiEmailer.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AiEmailer.Api.Controllers
{
    [ApiController]
    [Route("api/menu")]
    [Authorize]
    public class MenuController : ControllerBase
    {
        private readonly IMenuService _menuService;
        public MenuController(IMenuService menuService)
        {
            _menuService = menuService;
        }

        [HttpGet("list")]
        public async Task<ActionResult<List<MenuListItem>>> List()
        {
            var menus = await _menuService.ListAsync();
            return Ok(menus);
        }

        [HttpPost("addupd")]
        public async Task<ActionResult> AddUpd([FromBody] MenuAddUpdRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.MenuName)) return BadRequest("Menu name is required.");
            if (string.IsNullOrWhiteSpace(request.MenuKey)) return BadRequest("Menu key is required.");

            var userId = GetCurrentUserId();
            await _menuService.AddUpdAsync(request, userId);
            return Ok();
        }

        [HttpDelete("{idMenu:int}")]
        public async Task<ActionResult> Delete(int idMenu)
        {
            if (idMenu <= 0) return BadRequest("A valid menu id is required.");
            await _menuService.DeleteAsync(idMenu);
            return Ok();
        }

        // Reuse whatever claim your RoleController's GetCurrentUserId() (from our earlier step) resolves to —
        // keep this identical across controllers so createdby/modifiedby stay consistent.
        private int GetCurrentUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? throw new InvalidOperationException("User id claim not found.");
            return int.Parse(claim);
        }
    }
}
