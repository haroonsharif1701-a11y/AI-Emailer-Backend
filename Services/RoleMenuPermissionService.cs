using System.Data;
using AiEmailer.Api.Data;
using AiEmailer.Api.Models;
using Microsoft.Data.SqlClient;

namespace AiEmailer.Api.Services;

public sealed class RoleMenuPermissionService(DB db, ICurrentAccessService accessService) : IRoleMenuPermissionService
{
    public async Task<RolePermissionSet> ListAsync(int idRole)
    {
        var access = await accessService.GetAsync() ?? throw new UnauthorizedAccessException();
        await using var connection = new SqlConnection(db.GetConnectionString());
        await using var command = new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM dbo.skey_role WHERE id_role=@role AND id_org=@org)
                THROW 51003, 'Role not found in your organization.', 1;
            SELECT permission_version FROM dbo.skey_role WHERE id_role=@role AND id_org=@org;
            SELECT id_menu,can_view,can_add,can_update,can_delete FROM dbo.skey_role_menu_permission WHERE id_role=@role AND id_org=@org;
            """, connection);
        command.Parameters.Add("@role", SqlDbType.Int).Value = idRole;
        command.Parameters.Add("@org", SqlDbType.Int).Value = access.OrganizationId;
        await connection.OpenAsync();
        // A repeatable snapshot prevents combining a revision with a different permission set.
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        command.Transaction = transaction;
        long revision;
        var rows = new List<RoleMenuPermissionItem>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            await reader.ReadAsync();
            revision = reader.GetInt64(0);
            await reader.NextResultAsync();
            while (await reader.ReadAsync())
                rows.Add(new(reader.GetInt32(0), reader.GetBoolean(1), reader.GetBoolean(2), reader.GetBoolean(3), reader.GetBoolean(4)));
        }
        await transaction.CommitAsync();
        return new(revision, rows);
    }

    public async Task SaveAsync(RoleMenuPermissionSaveRequest request, int userId)
    {
        var access = await accessService.GetAsync() ?? throw new UnauthorizedAccessException();
        var table = new DataTable();
        table.Columns.Add("id_menu", typeof(int));
        foreach (var name in new[] { "can_view", "can_add", "can_update", "can_delete" }) table.Columns.Add(name, typeof(bool));
        foreach (var item in request.Permissions) table.Rows.Add(item.IdMenu, item.View, item.Add, item.Update, item.Delete);
        await using var connection = new SqlConnection(db.GetConnectionString());
        await using var command = new SqlCommand("dbo.rbac_permission_save", connection) { CommandType = CommandType.StoredProcedure };
        command.Parameters.Add("@id_user", SqlDbType.Int).Value = userId;
        command.Parameters.Add("@id_org", SqlDbType.Int).Value = access.OrganizationId;
        command.Parameters.Add("@id_role", SqlDbType.Int).Value = request.IdRole;
        command.Parameters.Add("@version", SqlDbType.BigInt).Value = request.PermissionVersion;
        var parameter = command.Parameters.AddWithValue("@permissions", table);
        parameter.SqlDbType = SqlDbType.Structured;
        parameter.TypeName = "dbo.RoleMenuPermissionV2Type";
        await connection.OpenAsync();
        await command.ExecuteNonQueryAsync();
    }
}
