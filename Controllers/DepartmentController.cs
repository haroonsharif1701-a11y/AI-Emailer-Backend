using AiEmailer.Api.Models;
using AiEmailer.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.Data.SqlClient;

namespace AiEmailer.Api.Controllers;

[ApiController]
[Route("api/department")]
[Authorize]
public sealed class DepartmentController : ControllerBase
{
    private readonly IDepartmentService _service;

    public DepartmentController(IDepartmentService service) => _service = service;

    private string CurrentUuqid =>
        HttpContext.Session.GetString("Uuqid")
        ?? User.FindFirstValue("uuqid")
        ?? throw new UnauthorizedAccessException("User session UUID was not found.");

    [HttpGet("list")]
    public async Task<ActionResult<List<DepartmentListItem>>> List([FromQuery] int? idDept = null)
        => Ok(await _service.ListAsync(CurrentUuqid, idDept));

    [HttpPost("addupd")]
    public async Task<IActionResult> AddUpdate([FromBody] DepartmentSaveRequest request)
    {
        try
        {
            await _service.AddUpdateAsync(request, CurrentUuqid);
            return Ok(new { message = "Department saved." });
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
