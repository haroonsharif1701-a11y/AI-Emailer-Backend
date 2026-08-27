using System.Data;
using AiEmailer.Api.Models;
using Microsoft.Data.SqlClient;

namespace AiEmailer.Api.Services;

public class RoleService : IRoleService
{
    private readonly string _connectionString;

    public RoleService(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured in appsettings.json.");
    }

    public async Task<List<RoleListItem>> ListAsync()
    {
        var results = new List<RoleListItem>();

        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand("role_list", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@id_role", DBNull.Value);

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            results.Add(new RoleListItem(
                IdRole: reader.GetInt32(reader.GetOrdinal("id_role")),
                RoleName: reader.GetString(reader.GetOrdinal("role_name"))
            ));
        }

        return results;
    }

    public async Task AddUpdateAsync(RoleModel.RoleSaveRequest request, int actingUserId)
    {
        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand("role_addupd", conn) { CommandType = CommandType.StoredProcedure };

        cmd.Parameters.AddWithValue("@id_role", request.IdRole);
        cmd.Parameters.AddWithValue("@role_name", request.RoleName);
        cmd.Parameters.AddWithValue("@createdby", actingUserId);
        cmd.Parameters.AddWithValue("@modifiedby", actingUserId);

        await conn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }

    // ---- NEW ----
    public async Task DeleteAsync(int idRole)
    {
        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand("role_delete", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@id_role", idRole);

        await conn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }
}
