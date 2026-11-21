using AiEmailer.Api.Models;

namespace AiEmailer.Api.Services;

public interface ICurrentAccessService
{
    Task<AccessSnapshot?> GetAsync(CancellationToken cancellationToken = default);
}
