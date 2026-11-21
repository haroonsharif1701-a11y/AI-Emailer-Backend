using System.Data;
using System.Security.Claims;
using AiEmailer.Api.Data;
using AiEmailer.Api.Models;
using Microsoft.Data.SqlClient;

namespace AiEmailer.Api.Services;

// Scoped to one HTTP request. Never cache permissions across requests or in a JWT/session.
public sealed class CurrentAccessService(DB db, IHttpContextAccessor context) : ICurrentAccessService
{
    private Task<AccessSnapshot?>? _current;

    public Task<AccessSnapshot?> GetAsync(CancellationToken cancellationToken = default) =>
        _current ??= LoadAsync(cancellationToken);

    private async Task<AccessSnapshot?> LoadAsync(CancellationToken cancellationToken)
    {
        var principal = context.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true ||
            !int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub"), out var userId) ||
            !int.TryParse(principal.FindFirstValue("org_id"), out var orgId)) return null;

        await using var connection = new SqlConnection(db.GetConnectionString());
        await using var command = new SqlCommand("dbo.rbac_current_access", connection)
        { CommandType = CommandType.StoredProcedure };
        command.Parameters.Add("@id_user", SqlDbType.Int).Value = userId;
        command.Parameters.Add("@id_org", SqlDbType.Int).Value = orgId;
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var roleId = reader.GetInt32(0);
        var revision = reader.GetInt64(1);
        var permissions = new Dictionary<string, MenuPermissions>(StringComparer.Ordinal);
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            permissions.Add(reader.GetString(0), new(reader.GetBoolean(1), reader.GetBoolean(2), reader.GetBoolean(3), reader.GetBoolean(4)));
        return new(orgId, roleId, revision, permissions);
    }
}
