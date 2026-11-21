using System.Data;
using AiEmailer.Api.Models;
using Microsoft.Data.SqlClient;
using AiEmailer.Api.Data;

namespace AiEmailer.Api.Services;

public sealed class TeamService : ITeamService
{
    private readonly string _connectionString;

    public TeamService(DB db)
    {
        _connectionString = db.GetConnectionString()
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured in appsettings.json.");
    }

    public async Task<List<TeamListItem>> ListAsync(string uuqid, int? idTeam = null, int? idDept = null)
    {
        var results = new List<TeamListItem>();

        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand("team_list", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add("@id_team", SqlDbType.Int).Value = (object?)idTeam ?? DBNull.Value;
        cmd.Parameters.Add("@id_dept", SqlDbType.Int).Value = (object?)idDept ?? DBNull.Value;
        cmd.Parameters.Add("@uuqid", SqlDbType.UniqueIdentifier).Value = ParseUuqid(uuqid);

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            results.Add(new TeamListItem(
                reader.GetInt32(reader.GetOrdinal("id_team")),
                reader.GetString(reader.GetOrdinal("team")),
                reader.GetInt32(reader.GetOrdinal("id_org")),
                reader.GetInt32(reader.GetOrdinal("id_dept")),
                reader.GetString(reader.GetOrdinal("dept_name")),
                reader.GetInt32(reader.GetOrdinal("total_members")),
                reader.GetDateTime(reader.GetOrdinal("createddt")),
                reader.GetInt32(reader.GetOrdinal("createdby")),
                reader.IsDBNull(reader.GetOrdinal("modifieddt")) ? null : reader.GetDateTime(reader.GetOrdinal("modifieddt")),
                reader.IsDBNull(reader.GetOrdinal("modifiedby")) ? null : reader.GetInt32(reader.GetOrdinal("modifiedby"))
            ));
        }

        return results;
    }

    public async Task AddUpdateAsync(TeamSaveRequest request, string uuqid)
    {
        var name = request.Team?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Team name is required.");

        if (request.IdDept <= 0)
            throw new ArgumentException("Department is required.");

        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand("team_addupd", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add("@id_team", SqlDbType.Int).Value = request.IdTeam;
        cmd.Parameters.Add("@team", SqlDbType.VarChar, 150).Value = name;
        cmd.Parameters.Add("@id_dept", SqlDbType.Int).Value = request.IdDept;
        cmd.Parameters.Add("@uuqid", SqlDbType.UniqueIdentifier).Value = ParseUuqid(uuqid);

        await conn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<TeamMemberListItem>> ListMembersAsync(int idTeam, string uuqid)
    {
        var results = new List<TeamMemberListItem>();

        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand("team_member_list", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add("@id_team", SqlDbType.Int).Value = idTeam;
        cmd.Parameters.Add("@uuqid", SqlDbType.UniqueIdentifier).Value = ParseUuqid(uuqid);

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            results.Add(new TeamMemberListItem(
                reader.GetInt32(reader.GetOrdinal("id_team_member")),
                reader.GetInt32(reader.GetOrdinal("id_team")),
                reader.GetInt32(reader.GetOrdinal("id_user")),
                reader.GetString(reader.GetOrdinal("fullname")),
                reader.GetString(reader.GetOrdinal("username")),
                reader.IsDBNull(reader.GetOrdinal("id_dept")) ? null : reader.GetInt32(reader.GetOrdinal("id_dept")),
                reader.GetInt32(reader.GetOrdinal("id_order")),
                reader.GetDateTime(reader.GetOrdinal("createddt")),
                reader.GetInt32(reader.GetOrdinal("createdby")),
                reader.IsDBNull(reader.GetOrdinal("modifieddt")) ? null : reader.GetDateTime(reader.GetOrdinal("modifieddt")),
                reader.IsDBNull(reader.GetOrdinal("modifiedby")) ? null : reader.GetInt32(reader.GetOrdinal("modifiedby"))
            ));
        }

        return results;
    }

    public async Task SyncMembersAsync(TeamMemberSyncRequest request, string uuqid)
    {
        if (request.IdTeam <= 0)
            throw new ArgumentException("Team is required.");

        var selected = request.UserIds?.Distinct().ToHashSet() ?? new HashSet<int>();
        var uuid = ParseUuqid(uuqid);

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            // The stored procedure enforces both team-org and user-org ownership.
            // We first read the current memberships, then only insert missing users
            // and remove users that were unchecked.
            var current = new Dictionary<int, int>();
            await using (var readCmd = new SqlCommand("team_member_list", conn, (SqlTransaction)tx)
            {
                CommandType = CommandType.StoredProcedure
            })
            {
                readCmd.Parameters.Add("@id_team", SqlDbType.Int).Value = request.IdTeam;
                readCmd.Parameters.Add("@uuqid", SqlDbType.UniqueIdentifier).Value = uuid;

                await using var reader = await readCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    current[reader.GetInt32(reader.GetOrdinal("id_user"))] =
                        reader.GetInt32(reader.GetOrdinal("id_team_member"));
                }
            }

            foreach (var userId in selected.Except(current.Keys))
            {
                await using var addCmd = new SqlCommand("team_member_addupd", conn, (SqlTransaction)tx)
                {
                    CommandType = CommandType.StoredProcedure
                };
                addCmd.Parameters.Add("@id_team_member", SqlDbType.Int).Value = 0;
                addCmd.Parameters.Add("@id_team", SqlDbType.Int).Value = request.IdTeam;
                addCmd.Parameters.Add("@id_user", SqlDbType.Int).Value = userId;
                addCmd.Parameters.Add("@id_order", SqlDbType.Int).Value = 0;
                addCmd.Parameters.Add("@uuqid", SqlDbType.UniqueIdentifier).Value = uuid;
                await addCmd.ExecuteNonQueryAsync();
            }

            foreach (var userId in current.Keys.Except(selected))
            {
                await using var deleteCmd = new SqlCommand("team_member_delete", conn, (SqlTransaction)tx)
                {
                    CommandType = CommandType.StoredProcedure
                };
                deleteCmd.Parameters.Add("@id_team", SqlDbType.Int).Value = request.IdTeam;
                deleteCmd.Parameters.Add("@id_user", SqlDbType.Int).Value = userId;
                deleteCmd.Parameters.Add("@uuqid", SqlDbType.UniqueIdentifier).Value = uuid;
                await deleteCmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private static Guid ParseUuqid(string uuqid)
        => Guid.TryParse(uuqid, out var value)
            ? value
            : throw new UnauthorizedAccessException("User session UUID is invalid.");
}
