using AiEmailer.Api.Models;

namespace AiEmailer.Api.Services
{
    public interface IMenuService
    {
        Task<List<MenuListItem>> ListAsync();
        Task AddUpdAsync(MenuAddUpdRequest request, int userId);
        Task DeleteAsync(int idMenu);
    }
}
