using AiEmailer.Api.Models;
using Microsoft.Data.SqlClient;
using System.Data;
using AiEmailer.Api.Data;

namespace AiEmailer.Api.Services
{
    public class MenuService : IMenuService
    {
        private readonly string _connectionString;
        public MenuService(DB db)
        {
            _connectionString = db.GetConnectionString()
                ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured in appsettings.json.");
        }

        public async Task<List<MenuListItem>> ListAsync()
        {
            var results = new List<MenuListItem>();
            await using var conn = new SqlConnection(_connectionString);
            await using var cmd = new SqlCommand("menu_list", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@id_menu", DBNull.Value);
            await conn.OpenAsync();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new MenuListItem(
                    IdMenu: reader.GetInt32(reader.GetOrdinal("id_menu")),
                    MenuName: reader.GetString(reader.GetOrdinal("menu_name")),
                    MenuKey: reader.GetString(reader.GetOrdinal("menu_key")),
                    MenuScript: reader.IsDBNull(reader.GetOrdinal("menu_script")) ? null : reader.GetString(reader.GetOrdinal("menu_script")),
                    IconClass: reader.IsDBNull(reader.GetOrdinal("icon_class")) ? null : reader.GetString(reader.GetOrdinal("icon_class")),
                    IdParentMenu: reader.IsDBNull(reader.GetOrdinal("id_parent_menu")) ? null : reader.GetInt32(reader.GetOrdinal("id_parent_menu")),
                    MenuLevel: reader.GetInt32(reader.GetOrdinal("menu_level")),
                    OrderId: reader.GetInt32(reader.GetOrdinal("order_id")),
                    IsEnable: reader.GetBoolean(reader.GetOrdinal("is_enable"))
                ));
            }
            return results;
        }

        public async Task AddUpdAsync(MenuAddUpdRequest request, int userId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await using var cmd = new SqlCommand("menu_addupd", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@id_menu", request.IdMenu);
            cmd.Parameters.AddWithValue("@menu_name", request.MenuName);
            cmd.Parameters.AddWithValue("@menu_key", request.MenuKey);
            cmd.Parameters.AddWithValue("@menu_script", (object?)request.MenuScript ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@icon_class", (object?)request.IconClass ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id_parent_menu", (object?)request.IdParentMenu ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@menu_level", request.MenuLevel);
            cmd.Parameters.AddWithValue("@order_id", request.OrderId);
            cmd.Parameters.AddWithValue("@is_enable", request.IsEnable);
            cmd.Parameters.AddWithValue("@createdby", userId);
            cmd.Parameters.AddWithValue("@modifiedby", userId);
            await conn.OpenAsync();
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteAsync(int idMenu)
        {
            await using var conn = new SqlConnection(_connectionString);
            await using var cmd = new SqlCommand("menu_delete", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@id_menu", idMenu);
            await conn.OpenAsync();
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
