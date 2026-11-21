using System.Data;
using AiEmailer.Api.Models;
using Microsoft.Data.SqlClient;
using AiEmailer.Api.Data;

namespace AiEmailer.Api.Services;

public class RoleService : IRoleService
{
    private readonly string _connectionString;
    private readonly ICurrentAccessService _access;

    public RoleService(DB db, ICurrentAccessService access)
    {
        _access = access;
        _connectionString = db.GetConnectionString()
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured in appsettings.json.");
    }

    public async Task<List<RoleListItem>> ListAsync()
    {
        var results = new List<RoleListItem>();

        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand("SELECT id_role,role_name FROM dbo.skey_role WHERE id_org=@id_org ORDER BY role_name", conn);
        cmd.Parameters.AddWithValue("@id_org", (await _access.GetAsync())!.OrganizationId);

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
        await using var cmd = new SqlCommand("rbac_role_addupd", conn) { CommandType = CommandType.StoredProcedure };

        cmd.Parameters.AddWithValue("@id_org", (await _access.GetAsync())!.OrganizationId);
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
        await using var cmd = new SqlCommand("rbac_role_delete", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@id_org", (await _access.GetAsync())!.OrganizationId);
        cmd.Parameters.AddWithValue("@id_role", idRole);

        await conn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }
}
