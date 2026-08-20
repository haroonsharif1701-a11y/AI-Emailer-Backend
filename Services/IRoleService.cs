using AiEmailer.Api.Models;

namespace AiEmailer.Api.Services;

public interface IRoleService
{
    Task<List<RoleListItem>> ListAsync();
}
