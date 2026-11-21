using Microsoft.Data.SqlClient;
using System.Data;
using AiEmailer.Api.Data;

namespace AiEmailer.Api.Services;

public class AttachmentRepository : IAttachmentRepository
{
    private readonly string _connectionString;

    public AttachmentRepository(DB db)
    {
        _connectionString = db.GetConnectionString()
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    public async Task<int> AddAsync(AttachmentRecordInput input, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand("dbo.attachment_analyzer_addupd", conn) { CommandType = CommandType.StoredProcedure };

        cmd.Parameters.AddWithValue("@id_attachment", DBNull.Value);
        cmd.Parameters.AddWithValue("@id_org", input.IdOrg);
        cmd.Parameters.AddWithValue("@createdby", input.CreatedBy);
        cmd.Parameters.AddWithValue("@file_name", input.FileName);
        cmd.Parameters.AddWithValue("@file_path", (object?)input.FilePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@file_type", (object?)input.FileType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@file_size", (object?)input.FileSize ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@attachment_summary", (object?)input.AttachmentSummary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@attachment_description", (object?)input.AttachmentDescription ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@analyzed_on", (object?)input.AnalyzedOn ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@analysis_status", input.AnalysisStatus);
        cmd.Parameters.AddWithValue("@analysis_error", (object?)input.AnalysisError ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@modifiedby", DBNull.Value);

        await conn.OpenAsync(ct);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
            return reader.GetInt32(reader.GetOrdinal("id_attachment"));

        throw new InvalidOperationException("attachment_analyzer_addupd did not return an id_attachment.");
    }

    public async Task<IReadOnlyList<AttachmentRecord>> ListAsync(int? idAttachment = null, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand("dbo.attachment_analyzer_list", conn) { CommandType = CommandType.StoredProcedure };

        // @uuqid's purpose isn't used inside the proc body — passing empty
        // for now. See gap #2 below.
        cmd.Parameters.AddWithValue("@uuqid", "");
        cmd.Parameters.AddWithValue("@id_attachment", (object?)idAttachment ?? DBNull.Value);

        var results = new List<AttachmentRecord>();
        await conn.OpenAsync(ct);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new AttachmentRecord
            {
                IdAttachment = reader.GetInt32(reader.GetOrdinal("id_attachment")),
                IdOrg = reader.GetInt32(reader.GetOrdinal("id_org")),
                CreatedBy = reader.GetInt32(reader.GetOrdinal("createdby")),
                CreatedByName = reader["createdby_name"] as string,
                FileName = reader["file_name"] as string ?? "",
                FilePath = reader["file_path"] as string,
                FileType = reader["file_type"] as string,
                FileSize = reader["file_size"] as long?,
                AttachmentSummary = reader["attachment_summary"] as string,
                AttachmentDescription = reader["attachment_description"] as string,
                AnalyzedOn = reader["analyzed_on"] as DateTime?,
                AnalysisStatus = reader["analysis_status"] as string,
                AnalysisError = reader["analysis_error"] as string,
                CreatedDt = (DateTime)reader["createddt"],
                ModifiedDt = reader["modifieddt"] as DateTime?,
            });
        }
        return results;
    }

    public async Task DeleteAsync(int idAttachment, int idOrg, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand("dbo.attachment_analyzer_delete", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@id_attachment", idAttachment);
        cmd.Parameters.AddWithValue("@id_org", idOrg);
        await conn.OpenAsync(ct);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}