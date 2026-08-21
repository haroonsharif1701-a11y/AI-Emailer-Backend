using AiEmailer.Api.Models;

namespace AiEmailer.Api.Services;

public interface IUserService
{
    Task<List<UserListItem>> ListAsync(int idOrg);
    Task<UserDetail?> GetAsync(int idUser, string? uuqid);
    Task AddUpdateAsync(UserSaveRequest request, int actingUserId, string uuqid);
    Task DeleteAsync(int idUser);
}
