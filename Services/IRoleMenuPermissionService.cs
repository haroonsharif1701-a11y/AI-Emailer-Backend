using AiEmailer.Api.Models;

namespace AiEmailer.Api.Services
{
    public interface IRoleMenuPermissionService
    {
        Task<RolePermissionSet> ListAsync(int idRole);
        Task SaveAsync(RoleMenuPermissionSaveRequest request, int userId);
    }
}
