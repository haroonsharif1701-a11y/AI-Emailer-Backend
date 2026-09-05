using AiEmailer.Api.Models;

namespace AiEmailer.Api.Services
{
    public interface IRoleMenuPermissionService
    {
        Task<List<RoleMenuPermissionItem>> ListAsync(int idRole);
        Task SaveAsync(RoleMenuPermissionSaveRequest request, int userId);
    }
}
