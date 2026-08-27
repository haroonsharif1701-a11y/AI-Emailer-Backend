using AiEmailer.Api.Models;

namespace AiEmailer.Api.Services;

public interface IRoleService
{
    Task<List<RoleListItem>> ListAsync();

    Task AddUpdateAsync(RoleModel.RoleSaveRequest request, int actingUserId);
    Task DeleteAsync(int idRole);
}
