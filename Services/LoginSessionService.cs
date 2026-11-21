using System.Data;
using System.Security.Cryptography;
using System.Text;
using AiEmailer.Api.Data;
using AiEmailer.Api.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;

namespace AiEmailer.Api.Services;

public sealed class LoginSessionService(DB db) : ILoginSessionService
{
    public const int LifetimeMinutes = 60;

    public async Task<Guid> CreateAsync(int userId, int orgId, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        await using var connection = new SqlConnection(db.GetConnectionString());
        await using var command = new SqlCommand("""
            INSERT dbo.skey_login_session(id_session,id_user,id_org,expires_at)
            SELECT @session,@user,@org,DATEADD(MINUTE,60,SYSUTCDATETIME())
            WHERE EXISTS(SELECT 1 FROM dbo.skey_user WHERE id_user=@user AND id_org=@org AND is_active=1);
            IF @@ROWCOUNT=0 THROW 51001,'Active account required.',1;
            """, connection);
        command.Parameters.AddWithValue("@session", id);
        command.Parameters.AddWithValue("@user", userId);
        command.Parameters.AddWithValue("@org", orgId);
        await connection.OpenAsync(ct);
        await command.ExecuteNonQueryAsync(ct);
        return id;
    }

    public async Task<bool> IsActiveAsync(Guid sessionId, int userId, int orgId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(db.GetConnectionString());
        await using var command = new SqlCommand("""
            SELECT COUNT(*) FROM dbo.skey_login_session s
            JOIN dbo.skey_user u ON u.id_user=s.id_user AND u.id_org=s.id_org
            WHERE s.id_session=@session AND s.id_user=@user AND s.id_org=@org
              AND s.revoked_at IS NULL AND s.expires_at>SYSUTCDATETIME() AND u.is_active=1
            """, connection);
        command.Parameters.AddWithValue("@session", sessionId);
        command.Parameters.AddWithValue("@user", userId);
        command.Parameters.AddWithValue("@org", orgId);
        await connection.OpenAsync(ct);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct)) == 1;
    }

    public async Task RevokeAsync(Guid sessionId, int userId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(db.GetConnectionString());
        await using var command = new SqlCommand("UPDATE dbo.skey_login_session SET revoked_at=COALESCE(revoked_at,SYSUTCDATETIME()) WHERE id_session=@session AND id_user=@user", connection);
        command.Parameters.AddWithValue("@session", sessionId);
        command.Parameters.AddWithValue("@user", userId);
        await connection.OpenAsync(ct);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<string> CreateHandoffAsync(int userId, int orgId, string challenge, CancellationToken ct)
    {
        var code = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        await using var connection = new SqlConnection(db.GetConnectionString());
        await using var command = new SqlCommand("""
            DELETE dbo.skey_oauth_handoff WHERE expires_at<SYSUTCDATETIME();
            INSERT dbo.skey_oauth_handoff(code_hash,challenge,id_user,id_org,expires_at)
            VALUES(@hash,@challenge,@user,@org,DATEADD(SECOND,60,SYSUTCDATETIME()));
            """, connection);
        command.Parameters.Add("@hash", SqlDbType.Binary, 32).Value = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        command.Parameters.AddWithValue("@challenge", challenge);
        command.Parameters.AddWithValue("@user", userId);
        command.Parameters.AddWithValue("@org", orgId);
        await connection.OpenAsync(ct);
        await command.ExecuteNonQueryAsync(ct);
        return code;
    }

    public async Task<LoginResult?> ExchangeAsync(string code, string verifier, CancellationToken ct)
    {
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));
        await using var connection = new SqlConnection(db.GetConnectionString());
        await using var command = new SqlCommand("""
            DECLARE @identity TABLE(id_user INT,id_org INT);
            DELETE dbo.skey_oauth_handoff
            OUTPUT deleted.id_user,deleted.id_org INTO @identity
            WHERE code_hash=@hash AND challenge=@challenge COLLATE Latin1_General_100_BIN2 AND expires_at>SYSUTCDATETIME();
            SELECT u.id_user,u.username,u.fullname,u.uuqid,u.id_role,r.role_name,u.id_org
            FROM @identity i JOIN dbo.skey_user u ON u.id_user=i.id_user AND u.id_org=i.id_org
            JOIN dbo.skey_role r ON r.id_role=u.id_role AND r.id_org=u.id_org WHERE u.is_active=1;
            """, connection);
        command.Parameters.Add("@hash", SqlDbType.Binary, 32).Value = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        command.Parameters.AddWithValue("@challenge", challenge);
        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.IsDBNull(3)) return null;
        return new(LoginStatus.Success,reader.GetInt32(0),reader.GetString(1),reader.GetString(2),reader.GetGuid(3),reader.GetInt32(4),reader.GetString(5),reader.GetInt32(6));
    }
}
