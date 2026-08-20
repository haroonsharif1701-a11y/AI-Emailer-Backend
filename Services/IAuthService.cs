using AiEmailer.Api.Models;

namespace AiEmailer.Api.Services;

public interface IAuthService
{
    Task<LoginResult> LoginAsync(string login, string password);
}
