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
    [MenuPermission("view", "settings.permissions")]
        public async Task<ActionResult<List<MenuListItem>>> List()
        {
            var menus = await _menuService.ListAsync();
            return Ok(menus);
        }

        [HttpPost("addupd")]
        public ActionResult AddUpd([FromBody] MenuAddUpdRequest request) => Forbid();

        [HttpDelete("{idMenu:int}")]
        public ActionResult Delete(int idMenu) => Forbid();
    }
}
