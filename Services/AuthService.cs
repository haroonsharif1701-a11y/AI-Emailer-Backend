using AiEmailer.Api.Models;
using DocumentFormat.OpenXml.Office2016.Excel;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text;
using AiEmailer.Api.Data;

namespace AiEmailer.Api.Services;

public class AuthService : IAuthService
{
    private const int MaxFailedAttempts = 5;

    private readonly string _connectionString;
    private readonly ILogger<AuthService> _logger;

    public AuthService(DB db, ILogger<AuthService> logger)
    {
        _connectionString = db.GetConnectionString()
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default is not configured in appsettings.json.");
        _logger = logger;
    }

    public async Task<LoginResult> LoginAsync(string login, string password)
    {
        var record = await FetchUserAsync(login);

        if (record is null)
        {
            // Same status as "wrong password" on purpose — confirming that a
            // username exists but the password was wrong (vs. the username
            // not existing at all) is a user-enumeration leak.
            // _logger.LogInformation("Login attempt for unknown username {Login}", login);
            var Logmsg = ("Login attempt for unknown username ", login);
            await UserLogDB(LoginStatus.InvalidCredentials, login, Logmsg.ToString(), 0, 0);
            return new LoginResult(LoginStatus.InvalidCredentials);
        }

        if (!record.IsActive)
        {
            // _logger.LogInformation("Login attempt for inactive account {Login}", login);
            var Logmsg = ("Login attempt for inactive account",login);
            await UserLogDB(LoginStatus.Inactive, record.Username, Logmsg.ToString(), record.IdUser, record.IdOrg);
            return new LoginResult(LoginStatus.Inactive);
        }

        if (record.FailedAttempts > MaxFailedAttempts)
        {
            //_logger.LogWarning("Login attempt for locked-out account {Login} ({Attempts} failed attempts)",
            //  login, record.FailedAttempts);
            var Logmsg = ("Login attempt for locked-out account failed attempts)", login,"Failed attempts:", record.FailedAttempts);
            await UserLogDB(LoginStatus.TooManyFailedAttempts, record.Username, Logmsg.ToString(), record.IdUser, record.IdOrg);
            return new LoginResult(LoginStatus.TooManyFailedAttempts);
        }

        var passwordOk = BCrypt.Net.BCrypt.Verify(password, Encoding.UTF8.GetString(record.PasswordHash));

        if (!passwordOk)
        {
            await RegisterFailedAttemptAsync(record.IdUser);
            _logger.LogInformation("Wrong password for {Login} ({Attempts} failed attempts now)",
                login, record.FailedAttempts + 1);
            var Logmsg = ("Wrong password for failed attempts now)", login, ":", record.FailedAttempts);
            await UserLogDB(LoginStatus.InvalidCredentials, record.Username, Logmsg.ToString(), record.IdUser, record.IdOrg);
            return new LoginResult(LoginStatus.InvalidCredentials);
        }
        
        await ResetFailedAttemptsAsync(record.IdUser);

        return new LoginResult(
            LoginStatus.Success,
            record.IdUser,
            record.Username,
            record.FullName,
            record.Uuqid,
            record.IdRole,
            record.RoleName,
            record.IdOrg
        );
    }

    private async Task<UserAuthRecord?> FetchUserAsync(string login)
    {
        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand("auth_user", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@login", login);

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new UserAuthRecord
        {
            IdUser = reader.GetInt32(reader.GetOrdinal("id_user")),
            Username = reader.GetString(reader.GetOrdinal("username")),
            FullName = reader.GetString(reader.GetOrdinal("fullname")),
            PasswordHash = (byte[])reader["password"],
            Uuqid = reader.GetGuid(reader.GetOrdinal("uuqid")),
            IdRole = reader.GetInt32(reader.GetOrdinal("id_role")),
            RoleName = reader.IsDBNull(reader.GetOrdinal("role_name")) ? null : reader.GetString(reader.GetOrdinal("role_name")),
            IdOrg = reader.GetInt32(reader.GetOrdinal("id_org")),
            IsActive = reader.GetBoolean(reader.GetOrdinal("is_active")),
            FailedAttempts = reader.GetInt32(reader.GetOrdinal("failed_attempts")),
        };
    }

    private async Task RegisterFailedAttemptAsync(int idUser)
    {
        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand("auth_user_register_failed_attempt", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@id_user", idUser);
        await conn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task ResetFailedAttemptsAsync(int idUser)
    {
        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand("auth_user_reset_failed_attempts", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@id_user", idUser);
        await conn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }

    //Logging User Details for Login

    private async Task  UserLogDB(LoginStatus loginStatus, string username, string Logmsg, int? IdUser, int? IdOrg) 
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await using var cmd = new SqlCommand("user_log_add", conn) { CommandType = CommandType.StoredProcedure };

            cmd.Parameters.AddWithValue("@status", loginStatus);
            cmd.Parameters.AddWithValue("@username", username);
            cmd.Parameters.AddWithValue("@logmsg", Logmsg);
            cmd.Parameters.AddWithValue("@id_user", IdUser);
            cmd.Parameters.AddWithValue("@id_org", IdOrg);

            await conn.OpenAsync();
            await cmd.ExecuteNonQueryAsync();

        }
        catch (Exception ex) {
            
        }
    }
}
