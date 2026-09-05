using AiEmailer.Api.Models;
using Microsoft.Data.SqlClient;
using System.Data;

namespace AiEmailer.Api.Services
{
    public class RoleMenuPermissionService : IRoleMenuPermissionService
    {
        private readonly string _connectionString;
        public RoleMenuPermissionService(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("Default")
                ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured in appsettings.json.");
        }

        public async Task<List<RoleMenuPermissionItem>> ListAsync(int idRole)
        {
            var results = new List<RoleMenuPermissionItem>();
            await using var conn = new SqlConnection(_connectionString);
            await using var cmd = new SqlCommand("role_menu_permission_list", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@id_role", idRole);
            await conn.OpenAsync();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new RoleMenuPermissionItem(
                    IdMenu: reader.GetInt32(reader.GetOrdinal("id_menu")),
                    Permission: reader.GetInt32(reader.GetOrdinal("permission"))
                ));
            }
            return results;
        }

        public async Task SaveAsync(RoleMenuPermissionSaveRequest request, int userId)
        {
            var table = new DataTable();
            table.Columns.Add("id_menu", typeof(int));
            table.Columns.Add("permission", typeof(int));
            foreach (var p in request.Permissions)
                table.Rows.Add(p.IdMenu, p.Permission);

            await using var conn = new SqlConnection(_connectionString);
            await using var cmd = new SqlCommand("role_menu_permission_savebulk", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@id_role", request.IdRole);
            cmd.Parameters.AddWithValue("@modifiedby", userId);
            var tvp = cmd.Parameters.AddWithValue("@permissions", table);
            tvp.SqlDbType = SqlDbType.Structured;
            tvp.TypeName = "dbo.RoleMenuPermissionType";
            await conn.OpenAsync();
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
