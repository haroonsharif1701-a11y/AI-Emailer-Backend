using System.Data;
using System.Text;
using AiEmailer.Api.Models;
using Microsoft.Data.SqlClient;
using AiEmailer.Api.Data;

namespace AiEmailer.Api.Services;

public class UserService : IUserService
{
    private readonly string _connectionString;
    private readonly IRoleService _roleService;
    private readonly ICurrentAccessService _access;
    private readonly IHttpContextAccessor _context;

    public UserService(DB db, IRoleService roleService, ICurrentAccessService access, IHttpContextAccessor context)
    {
        _connectionString = db.GetConnectionString()
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured in appsettings.json.");
        _roleService = roleService;
        _access = access;
        _context = context;
    }

    public async Task<List<UserListItem>> ListAsync(int idOrg)
    {
        // role_name isn't in user_list's result set — resolved here via
        // role_list rather than changing the stored procedure, since the
        // existing proc is already relied on elsewhere.
        var roles = await _roleService.ListAsync();
        var roleNameById = roles.ToDictionary(r => r.IdRole, r => r.RoleName);

        var results = new List<UserListItem>();

        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand("user_list", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@id_user", DBNull.Value);
        cmd.Parameters.AddWithValue("@id_org", idOrg);

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var idRole = reader.GetInt32(reader.GetOrdinal("id_role"));
            var isActiveOrdinal = reader.GetOrdinal("is_active");

            results.Add(new UserListItem(
                IdUser: reader.GetInt32(reader.GetOrdinal("id_user")),
                Username: reader.GetString(reader.GetOrdinal("username")),
                FullName: reader.GetString(reader.GetOrdinal("fullname")),
                IdRole: idRole,
                RoleName: roleNameById.TryGetValue(idRole, out var name) ? name : "Unknown role",
                OrgName: reader.GetString(reader.GetOrdinal("org_name")),
                IdDept: reader.IsDBNull("id_dept") ? null : reader.GetInt32("id_dept"),
                DeptName: reader.IsDBNull("dept_name") ? null : reader.GetString("dept_name"),
                IsActive: !reader.IsDBNull(isActiveOrdinal) && reader.GetBoolean(isActiveOrdinal)
            ));
        }

        return results;
    }

    public async Task<UserDetail?> GetAsync(int idUser, string? uuqid)
    {
        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand("user_list", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@id_user", idUser);
        cmd.Parameters.AddWithValue("@id_org", DBNull.Value);
        cmd.Parameters.AddWithValue("@uuqid", uuqid);

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync()) return null;
        var isActiveOrdinal = reader.GetOrdinal("is_active");

        return new UserDetail(
            IdUser: reader.GetInt32(reader.GetOrdinal("id_user")),
            Username: reader.GetString(reader.GetOrdinal("username")),
            FullName: reader.GetString(reader.GetOrdinal("fullname")),
            IdRole: reader.GetInt32(reader.GetOrdinal("id_role")),
            RoleName: reader.GetString(reader.GetOrdinal("role_name")),
            IdOrg: reader.GetInt32(reader.GetOrdinal("id_org")),
            OrgName: reader.GetString(reader.GetOrdinal("org_name")),
            IdDept: reader.IsDBNull("id_dept") ? null : reader.GetInt32("id_dept"),
            DeptName: reader.IsDBNull("dept_name") ? null : reader.GetString("dept_name"),
            IsActive: !reader.IsDBNull(isActiveOrdinal) && reader.GetBoolean(isActiveOrdinal),
            Os: reader.IsDBNull(reader.GetOrdinal("os")) ? null : reader.GetString(reader.GetOrdinal("os")),
            RegId: reader.IsDBNull(reader.GetOrdinal("regid")) ? null : reader.GetString(reader.GetOrdinal("regid")),
            DeviceToken: reader.IsDBNull(reader.GetOrdinal("device_token")) ? null : reader.GetString(reader.GetOrdinal("device_token"))
        );
    }

    public async Task AddUpdateAsync(UserSaveRequest request, int actingUserId, string uuqid)
    {
        byte[]? passwordBytes = null;

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            var hash = BCrypt.Net.BCrypt.HashPassword(request.Password);
            passwordBytes = Encoding.UTF8.GetBytes(hash);
        }
        else if (request.IdUser == 0)
        {
            // user_addupd's @password parameter defaults to NULL, and on
            // INSERT that would create an account with no usable password —
            // silently succeeding here would be worse than failing loudly.
            throw new ArgumentException("Password is required when creating a new user.");
        }

        int idOrgToUse;
        object osToUse, regIdToUse, deviceTokenToUse;

        if (request.IdUser == 0)
        {
            // New users need an org. Since the frontend form doesn't collect
            // one (org selection wasn't in scope for this pass), new users
            // are created in the acting admin's own org — looked up here
            // rather than trusted from client input.
            var actingUser = await GetAsync(actingUserId, null)
                ?? throw new InvalidOperationException("Could not resolve the acting user's organization.");
            idOrgToUse = actingUser.IdOrg;
            osToUse = DBNull.Value;
            regIdToUse = DBNull.Value;
            deviceTokenToUse = DBNull.Value;
        }
        else
        {
            // CRITICAL: user_addupd's UPDATE branch does
            // "SET id_org = @id_org, os = @os, ..." unconditionally — unlike
            // @password, these are NOT wrapped in COALESCE. Passing DBNull
            // here would silently wipe the user's org/os/regid/device_token
            // on every single edit. Fetching and passing back the existing
            // values is what prevents that.
            var existing = await GetAsync(request.IdUser, null)
                ?? throw new ArgumentException($"User {request.IdUser} not found.");
            idOrgToUse = existing.IdOrg;
            osToUse = (object?)existing.Os ?? DBNull.Value;
            regIdToUse = (object?)existing.RegId ?? DBNull.Value;
            deviceTokenToUse = (object?)existing.DeviceToken ?? DBNull.Value;
        }

        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand("rbac_user_addupd", conn) { CommandType = CommandType.StoredProcedure };

        cmd.Parameters.AddWithValue("@id_user", request.IdUser);
        cmd.Parameters.AddWithValue("@username", request.Username);
        cmd.Parameters.AddWithValue("@fullname", request.FullName);
        var passwordParam = cmd.Parameters.Add("@password", SqlDbType.VarBinary, 200);
        passwordParam.Value = (object?)passwordBytes ?? DBNull.Value;
        cmd.Parameters.AddWithValue("@uuqid", request.IdUser == 0 ? Guid.NewGuid() : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@id_role", request.IdRole);
        cmd.Parameters.AddWithValue("@id_org", idOrgToUse);
        cmd.Parameters.AddWithValue("@id_dept",(object?)request.IdDept ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@os", osToUse);
        cmd.Parameters.AddWithValue("@regid", regIdToUse);
        cmd.Parameters.AddWithValue("@device_token", deviceTokenToUse);
        cmd.Parameters.AddWithValue("@isactive", request.IsActive);
        cmd.Parameters.AddWithValue("@createdby", actingUserId);
        cmd.Parameters.AddWithValue("@modifiedby", actingUserId);

        await conn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int idUser)
    {
        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand("rbac_user_delete", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@id_user", idUser);
        cmd.Parameters.AddWithValue("@id_org", (await _access.GetAsync())!.OrganizationId);
        cmd.Parameters.AddWithValue("@actor", int.Parse(_context.HttpContext!.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value));
        await conn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }
    public async Task UpdateProfileAsync(int userId, int organizationId, ProfileUpdateRequest request)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("""
            UPDATE dbo.skey_user SET username=@username,fullname=@name,modifiedby=@id,modifieddt=GETDATE()
            WHERE id_user=@id AND id_org=@org AND is_active=1
            """, connection);
        command.Parameters.Add("@username", SqlDbType.VarChar, 200).Value = request.Username.Trim();
        command.Parameters.Add("@name", SqlDbType.VarChar, 300).Value = request.FullName.Trim();
        command.Parameters.Add("@id", SqlDbType.Int).Value = userId;
        command.Parameters.Add("@org", SqlDbType.Int).Value = organizationId;
        await connection.OpenAsync();
        await command.ExecuteNonQueryAsync();
    }
}
