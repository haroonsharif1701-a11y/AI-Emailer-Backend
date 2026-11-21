using System.Security.Claims;
using AiEmailer.Api.Data;
using AiEmailer.Api.Models;
using Microsoft.Data.SqlClient;
using System.Data;

namespace AiEmailer.Api.Services;

public interface IExternalAuthService
{
    Task<LoginResult> ProcessExternalLoginAsync(ClaimsPrincipal principal, string providerName);
}

public class ExternalAuthService : IExternalAuthService
{
    private readonly DB _db;
    private readonly ILogger<ExternalAuthService> _logger;

    public ExternalAuthService(DB db, ILogger<ExternalAuthService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<LoginResult> ProcessExternalLoginAsync(ClaimsPrincipal principal, string providerName)
    {
        // Extract the email from the external identity provider
        var email = principal.FindFirst(ClaimTypes.Email)?.Value
                  ?? principal.FindFirst("email")?.Value;

        if (string.IsNullOrEmpty(email))
        {
            _logger.LogError("External login failed: Missing email claim for provider {Provider}", providerName);
            return new LoginResult(LoginStatus.InvalidCredentials);
        }

        // We only care if the email exists in our database and belongs to a valid organization.
        // The auth_email stored procedure handles the check against skey_user.
        var orgUser = await ValidateUserByEmailAsync(email);
        if (orgUser == null)
        {
            _logger.LogWarning("External login blocked: User {Email} not found in authorized organization for provider {Provider}", email, providerName);
            return new LoginResult(LoginStatus.InvalidCredentials);
        }

        return new LoginResult(
            LoginStatus.Success,
            orgUser.IdUser,
            orgUser.Username,
            orgUser.FullName,
            orgUser.Uuqid,
            orgUser.IdRole,
            orgUser.RoleName,
            orgUser.IdOrg
        );
    }

    private async Task<UserAuthRecord?> ValidateUserByEmailAsync(string email)
    {
        try
        {
            using var conn = new SqlConnection(_db.GetConnectionString());
            using var cmd = new SqlCommand("SELECT TOP(2) u.id_user,u.id_org,u.username,u.fullname,u.uuqid,u.id_role,r.role_name FROM dbo.skey_user u JOIN dbo.skey_role r ON r.id_role=u.id_role AND r.id_org=u.id_org WHERE u.username=@email AND u.is_active=1 AND u.uuqid IS NOT NULL", conn);
            cmd.Parameters.AddWithValue("@email", email);

            await conn.OpenAsync();
            using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync()) return null;

            // Use dynamic conversion and column names to prevent InvalidCastException
            // This handles cases where DB returns Guid or Int64 instead of Int32
            var record = new UserAuthRecord
            {
                IdOrg = reader["id_org"] != DBNull.Value ? Convert.ToInt32(reader["id_org"]) : 0,
                IdUser = reader["id_user"] != DBNull.Value ? Convert.ToInt32(reader["id_user"]) : 0,
                Username = reader["username"]?.ToString() ?? "",
                FullName = reader["fullname"]?.ToString() ?? "",
                IdRole = Convert.ToInt32(reader["id_role"]),
                RoleName = reader["role_name"]?.ToString(),
                Uuqid = reader["uuqid"] != DBNull.Value ? (Guid)reader["uuqid"] : Guid.NewGuid()
            };
            return await reader.ReadAsync() ? null : record;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while validating user by email: {Email}", email);
            return null;
        }
    }
}