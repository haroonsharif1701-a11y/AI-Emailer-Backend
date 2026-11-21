using AiEmailer.Api.Models;
using AiEmailer.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AiEmailer.Api.Controllers;

[ApiController]
[Route("api/organization")]
[Authorize]
public class OrganizationController : ControllerBase
{
    private readonly IOrganizationService _organizationService;

    public OrganizationController(IOrganizationService organizationService)
    {
        _organizationService = organizationService;
    }

    private string CurrentUuqid =>
        User.FindFirstValue("uuqid")
        ?? throw new InvalidOperationException(
            "User UUID is missing from both session and claims.");

    [HttpGet("card-counts")]
    [MenuPermission("view", "organization")]
    public async Task<ActionResult<OrganizationCardCounts>> GetCardCounts()
    {
        var result =
            await _organizationService.GetCardCountsAsync(CurrentUuqid);

        if (result is null)
            return NotFound(new
            {
                message = "Organization information could not be found."
            });

        return Ok(result);
    }
}