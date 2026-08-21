using AiEmailer.Api.Models;

namespace AiEmailer.Api.Services
{
    public interface IOrganizationService
    {
        Task<OrganizationCardCounts?> GetCardCountsAsync(string uuqid);
    }
}
