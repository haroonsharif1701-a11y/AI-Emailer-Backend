namespace AiEmailer.Api.Models;

public record DepartmentListItem(
    int IdDept,
    string DeptName,
    int IdOrg
);

public record DepartmentSaveRequest(
    int IdDept,
    string DeptName
);

public record TeamListItem(
    int IdTeam,
    string Team,
    int IdOrg,
    int IdDept,
    string DeptName,
    int TotalMembers,
    DateTime CreatedDt,
    int CreatedBy,
    DateTime? ModifiedDt,
    int? ModifiedBy
);

public record TeamSaveRequest(
    int IdTeam,
    string Team,
    int IdDept
);

public record TeamMemberListItem(
    int IdTeamMember,
    int IdTeam,
    int IdUser,
    string FullName,
    string Username,
    int? IdDept,
    int IdOrder,
    DateTime CreatedDt,
    int CreatedBy,
    DateTime? ModifiedDt,
    int? ModifiedBy
);

public record TeamMemberSyncRequest(
    int IdTeam,
    List<int> UserIds
);
