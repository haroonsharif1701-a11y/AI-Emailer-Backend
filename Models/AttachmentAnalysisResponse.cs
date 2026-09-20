// Add this to Models/Dtos.cs, alongside the existing DTOs.

public class AttachmentAnalysisResponse
{
    // Set server-side after the model responds, not part of what we ask the LLM for.
    public string FileName { get; set; } = "";
    public string FileType { get; set; } = "";

    // NEW — set server-side after the DB/file save step.
    public int? Id { get; set; }
    public string? FileSize { get; set; }      // formatted, e.g. "2.4 MB"
    public DateTime? AnalyzedOn { get; set; }
    public string? PreviewUrl { get; set; }
    public string? DownloadUrl { get; set; }

    public string Summary { get; set; } = "";
    public string Description { get; set; } = "";
    public string ThreatLevel { get; set; } = "";
    public string ThreatNotes { get; set; } = "";
}
public class AttachmentListItem
{
    public int Id { get; set; }
    public string FileName { get; set; } = "";
    public string FileType { get; set; } = "";
    public string? FileSize { get; set; }
    public string Summary { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime? AnalyzedOn { get; set; }
    public string AnalysisStatus { get; set; } = "";
    public string PreviewUrl { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
}