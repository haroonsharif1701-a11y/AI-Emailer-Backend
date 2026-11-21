using AiEmailer.Api.Models;
namespace AiEmailer.Api.Services;

public interface ILoginSessionService
{
    Task<Guid> CreateAsync(int userId, int orgId, CancellationToken ct);
    Task<bool> IsActiveAsync(Guid sessionId, int userId, int orgId, CancellationToken ct);
    Task RevokeAsync(Guid sessionId, int userId, CancellationToken ct);
    Task<string> CreateHandoffAsync(int userId, int orgId, string challenge, CancellationToken ct);
    Task<LoginResult?> ExchangeAsync(string code, string verifier, CancellationToken ct);
}
