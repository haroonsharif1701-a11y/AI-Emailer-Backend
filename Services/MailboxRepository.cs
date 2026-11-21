using System.Data;
using System.Security.Cryptography;
using System.Text;
using AiEmailer.Api.Data;
using AiEmailer.Api.Models;
using Microsoft.Data.SqlClient;

namespace AiEmailer.Api.Services;

public sealed class MailboxRepository(DB db, MailboxTokenProtector protector) : IMailboxRepository
{
    private SqlCommand Command(SqlConnection connection, string sql, MailboxOwner owner)
    {
        var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@user", owner.UserId);
        command.Parameters.AddWithValue("@org", owner.OrgId);
        return command;
    }

    private MailboxConnection Read(SqlDataReader reader, MailboxOwner owner) => new(owner,
        reader.GetString(0), reader.GetString(1), protector.Unprotect(owner, reader.GetString(2)),
        reader.GetString(3), reader.GetGuid(4), reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc), reader.GetBoolean(7));

    public async Task<MailboxConnection?> GetAsync(MailboxOwner owner, CancellationToken ct)
    {
        await using var connection = new SqlConnection(db.GetConnectionString());
        await using var command = Command(connection, """
            SELECT account_id,email,refresh_token,scope,version,history_id,synced_at,reconnect_required
            FROM dbo.skey_mailbox_connection WHERE id_user=@user AND id_org=@org AND provider='Gmail'
            """, owner);
        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader, owner) : null;
    }

    public async Task SaveAsync(MailboxConnection item, CancellationToken ct)
    {
        await using var connection = new SqlConnection(db.GetConnectionString());
        await using var command = Command(connection, """
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            IF EXISTS(SELECT 1 FROM dbo.skey_mailbox_connection WITH(UPDLOCK,HOLDLOCK) WHERE id_user=@user AND id_org=@org)
                UPDATE dbo.skey_mailbox_connection SET provider='Gmail',account_id=@account,email=@email,
                    refresh_token=@token,scope=@scope,version=@version,history_id=NULL,synced_at=NULL,reconnect_required=0
                    WHERE id_user=@user AND id_org=@org;
            ELSE INSERT dbo.skey_mailbox_connection(id_user,id_org,account_id,email,refresh_token,scope,version)
                VALUES(@user,@org,@account,@email,@token,@scope,@version);
            COMMIT TRANSACTION;
            """, item.Owner);
        command.Parameters.AddWithValue("@account", item.AccountId);
        command.Parameters.AddWithValue("@email", item.Email);
        command.Parameters.AddWithValue("@token", protector.Protect(item.Owner, item.RefreshToken));
        command.Parameters.AddWithValue("@scope", item.Scope);
        command.Parameters.AddWithValue("@version", item.Version);
        await connection.OpenAsync(ct);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<MailboxConnection?> DeleteAsync(MailboxOwner owner, CancellationToken ct)
    {
        await using var connection = new SqlConnection(db.GetConnectionString());
        await using var command = Command(connection, """
            DELETE dbo.skey_mailbox_authorization WHERE id_user=@user AND id_org=@org;
            DELETE dbo.skey_mailbox_connection OUTPUT deleted.account_id,deleted.email,deleted.refresh_token,
                deleted.scope,deleted.version,deleted.history_id,deleted.synced_at,deleted.reconnect_required
            WHERE id_user=@user AND id_org=@org AND provider='Gmail';
            """, owner);
        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader, owner) : null;
    }

    public Task MarkReconnectAsync(MailboxConnection item, CancellationToken ct) => UpdateAsync(item,
        "reconnect_required=1", null, ct);
    public Task SaveSyncAsync(MailboxConnection item, string historyId, CancellationToken ct) => UpdateAsync(item,
        "history_id=@history,synced_at=SYSUTCDATETIME()", historyId, ct);

    private async Task UpdateAsync(MailboxConnection item, string set, string? history, CancellationToken ct)
    {
        await using var connection = new SqlConnection(db.GetConnectionString());
        await using var command = Command(connection,
            $"UPDATE dbo.skey_mailbox_connection SET {set} WHERE id_user=@user AND id_org=@org AND version=@version" +
            (history is null ? "" : " AND (history_id=@previous OR (history_id IS NULL AND @previous IS NULL))"), item.Owner);
        command.Parameters.AddWithValue("@version", item.Version);
        command.Parameters.AddWithValue("@history", (object?)history ?? DBNull.Value);
        command.Parameters.AddWithValue("@previous", (object?)item.HistoryId ?? DBNull.Value);
        await connection.OpenAsync(ct);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> CanReadAsync(MailboxOwner owner, CancellationToken ct)
    {
        await using var connection = new SqlConnection(db.GetConnectionString());
        await using var command = new SqlCommand("dbo.rbac_current_access", connection) { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@id_user", owner.UserId);
        command.Parameters.AddWithValue("@id_org", owner.OrgId);
        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return false;
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
            if (reader.GetString(0) == "inbox" && reader.GetBoolean(1)) return true;
        return false;
    }

    public async Task<IReadOnlyList<MailboxOwner>> ListOwnersAsync(CancellationToken ct)
    {
        await using var connection = new SqlConnection(db.GetConnectionString());
        await using var command = new SqlCommand("SELECT id_user,id_org FROM dbo.skey_mailbox_connection WHERE provider='Gmail' AND reconnect_required=0", connection);
        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var owners = new List<MailboxOwner>();
        while (await reader.ReadAsync(ct)) owners.Add(new(reader.GetInt32(0), reader.GetInt32(1)));
        return owners;
    }

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    public async Task CreateAuthorizationAsync(string state, string browserNonce, MailboxAuthorization item, CancellationToken ct)
    {
        await using var connection = new SqlConnection(db.GetConnectionString());
        await using var command = Command(connection, """
            DELETE dbo.skey_mailbox_authorization WHERE expires_at<SYSUTCDATETIME() OR (id_user=@user AND id_org=@org);
            INSERT dbo.skey_mailbox_authorization(state_hash,browser_hash,id_user,id_org,id_session,verifier,expires_at)
            VALUES(@state,@browser,@user,@org,@session,@verifier,DATEADD(MINUTE,10,SYSUTCDATETIME()));
            """, item.Owner);
        command.Parameters.AddWithValue("@state", Hash(state));
        command.Parameters.AddWithValue("@browser", Hash(browserNonce));
        command.Parameters.AddWithValue("@session", item.SessionId);
        command.Parameters.AddWithValue("@verifier", protector.Protect(item.Owner, item.Verifier));
        await connection.OpenAsync(ct);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<MailboxAuthorization?> ConsumeAuthorizationAsync(string state, string browserNonce, CancellationToken ct)
    {
        await using var connection = new SqlConnection(db.GetConnectionString());
        await using var command = new SqlCommand("""
            DELETE dbo.skey_mailbox_authorization OUTPUT deleted.id_user,deleted.id_org,deleted.id_session,deleted.verifier
            WHERE state_hash=@state AND browser_hash=@browser AND expires_at>SYSUTCDATETIME();
            """, connection);
        command.Parameters.AddWithValue("@state", Hash(state));
        command.Parameters.AddWithValue("@browser", Hash(browserNonce));
        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var owner = new MailboxOwner(reader.GetInt32(0), reader.GetInt32(1));
        return new(owner, reader.GetGuid(2), protector.Unprotect(owner, reader.GetString(3)));
    }
}
