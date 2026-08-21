using System.Data;
using Microsoft.Data.SqlClient;
using AiEmailer.Api.Models;

namespace AiEmailer.Api.Services;

public class OrganizationService : IOrganizationService
{
    private readonly IConfiguration _configuration;

    public OrganizationService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<OrganizationCardCounts?> GetCardCountsAsync(string uuqid)
    {
        if (string.IsNullOrWhiteSpace(uuqid))
            return null;

        var connectionString =
            _configuration.GetConnectionString("Default");

        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand("dbo.org_card_counts", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        command.Parameters.Add("@uuqid", SqlDbType.VarChar, 200).Value = uuqid;

        await connection.OpenAsync();

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return new OrganizationCardCounts(
            TotalUser: reader.GetInt32(reader.GetOrdinal("total_user")),
            TotalDept: reader.GetInt32(reader.GetOrdinal("total_dept")),
            TotalTeams: reader.GetInt32(reader.GetOrdinal("total_teams")),
            TotalAdmins: reader.GetInt32(reader.GetOrdinal("total_admins"))
        );
    }
}