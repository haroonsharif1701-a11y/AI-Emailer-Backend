using AiEmailer.Api.Models;

namespace AiEmailer.Api.Services;

public interface IDepartmentService
{
    Task<List<DepartmentListItem>> ListAsync(string uuqid, int? idDept = null);
    Task AddUpdateAsync(DepartmentSaveRequest request, string uuqid);
}

public interface ITeamService
{
    Task<List<TeamListItem>> ListAsync(string uuqid, int? idTeam = null, int? idDept = null);
    Task AddUpdateAsync(TeamSaveRequest request, string uuqid);
    Task<List<TeamMemberListItem>> ListMembersAsync(int idTeam, string uuqid);
    Task SyncMembersAsync(TeamMemberSyncRequest request, string uuqid);
}
