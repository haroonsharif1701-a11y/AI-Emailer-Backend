using System.Data;
using AiEmailer.Api.Models;
using Microsoft.Data.SqlClient;

namespace AiEmailer.Api.Services;

public sealed class DepartmentService : IDepartmentService
{
    private readonly string _connectionString;

    public DepartmentService(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured in appsettings.json.");
    }

    public async Task<List<DepartmentListItem>> ListAsync(string uuqid, int? idDept = null)
    {
        var results = new List<DepartmentListItem>();

        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand("dept_list", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add("@id_dept", SqlDbType.Int).Value = (object?)idDept ?? DBNull.Value;
        cmd.Parameters.Add("@uuqid", SqlDbType.UniqueIdentifier).Value = ParseUuqid(uuqid);

        await conn.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();

        var idDeptOrdinal = reader.GetOrdinal("id_dept");
        var deptNameOrdinal = reader.GetOrdinal("dept_name");
        var idOrgOrdinal = reader.GetOrdinal("id_org");

        while (await reader.ReadAsync())
        {
            results.Add(new DepartmentListItem(
                reader.GetInt32(idDeptOrdinal),
                reader.GetString(deptNameOrdinal),
                reader.GetInt32(idOrgOrdinal)
            ));
        }

        return results;
    }

    public async Task AddUpdateAsync(DepartmentSaveRequest request, string uuqid)
    {
        var name = request.DeptName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Department name is required.");

        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand("dept_addupd", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add("@id_dept", SqlDbType.Int).Value = request.IdDept;
        cmd.Parameters.Add("@dept_name", SqlDbType.VarChar, 200).Value = name;
        cmd.Parameters.Add("@uuqid", SqlDbType.UniqueIdentifier).Value = ParseUuqid(uuqid);

        await conn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }

    private static Guid ParseUuqid(string uuqid)
        => Guid.TryParse(uuqid, out var value)
            ? value
            : throw new UnauthorizedAccessException("User session UUID is invalid.");
}
