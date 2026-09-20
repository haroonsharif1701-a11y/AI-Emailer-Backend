namespace AiEmailer.Api.Services;

public interface IAttachmentRepository
{
    Task<int> AddAsync(AttachmentRecordInput input, CancellationToken ct = default);
    Task<IReadOnlyList<AttachmentRecord>> ListAsync(int? idAttachment = null, CancellationToken ct = default);
    Task DeleteAsync(int idAttachment, int idOrg, CancellationToken ct = default);
}

public class AttachmentRecordInput
{
    public int IdOrg { get; set; }
    public int CreatedBy { get; set; }
    public string FileName { get; set; } = "";       // original name, e.g. "Invoice.pdf"
    public string? FilePath { get; set; }             // stored/disk name, e.g. "20260905153012123_Invoice.pdf"
    public string? FileType { get; set; }
    public long? FileSize { get; set; }
    public string? AttachmentSummary { get; set; }
    public string? AttachmentDescription { get; set; }
    public DateTime? AnalyzedOn { get; set; }
    public string AnalysisStatus { get; set; } = "Pending";
    public string? AnalysisError { get; set; }
}

public class AttachmentRecord
{
    public int IdAttachment { get; set; }
    public int IdOrg { get; set; }
    public int CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public string FileName { get; set; } = "";        // original, display-ready
    public string? FilePath { get; set; }              // stored/disk name — used only to locate the physical file
    public string? FileType { get; set; }
    public long? FileSize { get; set; }
    public string? AttachmentSummary { get; set; }
    public string? AttachmentDescription { get; set; }
    public DateTime? AnalyzedOn { get; set; }
    public string? AnalysisStatus { get; set; }
    public string? AnalysisError { get; set; }
    public DateTime CreatedDt { get; set; }
    public DateTime? ModifiedDt { get; set; }
}