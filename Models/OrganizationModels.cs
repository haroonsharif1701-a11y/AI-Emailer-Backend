namespace AiEmailer.Api.Models;

public record OrganizationCardCounts(
    int TotalUser,
    int TotalDept,
    int TotalTeams,
    int TotalAdmins
);