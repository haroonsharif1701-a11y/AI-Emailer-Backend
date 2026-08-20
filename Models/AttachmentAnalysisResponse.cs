// Add this to Models/Dtos.cs, alongside the existing DTOs.

public class AttachmentAnalysisResponse
{
    // Set server-side after the model responds, not part of what we ask the LLM for.
    public string FileName { get; set; } = "";
    public string FileType { get; set; } = "";

    public string Summary { get; set; } = "";
    public string Description { get; set; } = "";
    public string ThreatLevel { get; set; } = "";
    public string ThreatNotes { get; set; } = "";
}
