using System.Text.Json;
using AiEmailer.Api.Models;
using AiEmailer.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AiEmailer.Api.Controllers;

[ApiController]
[Route("api/attachment")]
[Authorize]
public class AttachmentController : ControllerBase
{
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
    private const int MaxCharsSentToModel = 6000; // keeps local-model latency reasonable

    private static readonly string[] AllowedExtensions = { ".pdf", ".docx", ".xlsx", ".png", ".jpg", ".jpeg" };

    private readonly IAttachmentTextExtractor _extractor;
    private readonly ILlmProvider _llm;
    private readonly ILogger<AttachmentController> _logger;
    private readonly IFileStorageService _fileStorage;
    private readonly IAttachmentRepository _attachmentRepo;

    public AttachmentController(
        IAttachmentTextExtractor extractor,
        ILlmProvider llm,
        IFileStorageService fileStorage,
        IAttachmentRepository attachmentRepo,
        ILogger<AttachmentController> logger)
    {
        _extractor = extractor;
        _llm = llm;
        _fileStorage = fileStorage;
        _attachmentRepo = attachmentRepo;
        _logger = logger;
    }

    private AttachmentListItem ToListItem(AttachmentRecord r) => new()
    {
        Id = r.IdAttachment,
        FileName = r.FileName,
        FileType = r.FileType ?? "",
        FileSize = r.FileSize is long size ? FormatFileSize(size) : null,
        Summary = r.AttachmentSummary ?? "",
        Description = r.AttachmentDescription ?? "",
        AnalyzedOn = r.AnalyzedOn,
        AnalysisStatus = r.AnalysisStatus ?? "",
        PreviewUrl = $"/api/attachment/file/{r.IdAttachment}",
        DownloadUrl = $"/api/attachment/file/{r.IdAttachment}?download=true",
    };

    private static string GetContentType(string? fileType) => fileType?.ToLowerInvariant() switch
    {
        "pdf" => "application/pdf",
        "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "png" => "image/png",
        "jpg" or "jpeg" => "image/jpeg",
        _ => "application/octet-stream",
    };

    private static string FormatFileSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }
        return $"{size:0.#} {units[unitIndex]}";
    }

    // TODO: confirm these claim names against your actual JWT — see gap #3.
    private int GetCurrentOrgId() =>
        int.TryParse(User.FindFirst("id_org")?.Value ?? User.FindFirst("org_id")?.Value, out var orgId)
            ? orgId
            : throw new InvalidOperationException("Could not resolve id_org from the current user's claims.");

    private int GetCurrentUserId() =>
        int.TryParse(User.FindFirst("id_user")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId)
            ? userId
            : throw new InvalidOperationException("Could not resolve id_user from the current user's claims.");


    [HttpPost("analyze")]
    [MenuPermission("add", "attachments")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    public async Task<ActionResult<AttachmentAnalysisResponse>> Analyze(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "No file was uploaded." });
        }

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
        {
            return BadRequest(new { message = $"Unsupported file type '{ext}'. Supported: {string.Join(", ", AllowedExtensions)}" });
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return BadRequest(new { message = $"File exceeds the {MaxFileSizeBytes / 1024 / 1024}MB limit." });
        }

        string extractedText;
        try
        {
            await using var stream = file.OpenReadStream();
            extractedText = await _extractor.ExtractTextAsync(stream, file.FileName);
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract text from {FileName}", file.FileName);
            return BadRequest(new { message = $"Couldn't read '{file.FileName}' — the file may be corrupted or password-protected." });
        }

        if (string.IsNullOrWhiteSpace(extractedText))
        {
            return BadRequest(new { message = "No readable text found in this file." });
        }

        var truncated = extractedText.Length > MaxCharsSentToModel;
        var textForModel = truncated ? extractedText[..MaxCharsSentToModel] : extractedText;
        if (truncated)
        {
            _logger.LogInformation(
                "Truncated {FileName} from {Original} to {Truncated} chars before sending to the model",
                file.FileName, extractedText.Length, MaxCharsSentToModel);
        }

        var systemPrompt =
            "You analyze document contents for a business email assistant. Do the summary of the attachment in minimum 15-20 lines possibly. Respond with ONLY a JSON object. Make the description of the attachment of approx 2-3 lines possibly meaningful, " +
            "no markdown fences, no commentary, matching exactly this shape: " +
            "{\"summary\": string, \"description\": string, \"threatLevel\": \"Low\"|\"Medium\"|\"High\", \"threatNotes\": string}. " +
            "threatLevel should reflect signs of malicious content, scams, or social engineering in the " +
            "document text itself — not general business risk. Most ordinary business documents are Low.";

        var userPrompt = $"Filename: {file.FileName}\n\nDocument text:\n{textForModel}";

        try
        {
            var raw = await _llm.GenerateAsync(systemPrompt, userPrompt, ct);
            var cleaned = raw.Trim().Trim('`').Replace("json\n", "", StringComparison.OrdinalIgnoreCase);

            var parsed = JsonSerializer.Deserialize<AttachmentAnalysisResponse>(cleaned, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            if (parsed is null)
            {
                return StatusCode(502, new { message = "Model returned unparseable output." });
            }

            parsed.FileName = file.FileName;
            parsed.FileType = ext.TrimStart('.').ToUpperInvariant();

            // Persist the physical file + DB row. Deliberately non-fatal:
            // if storage/DB isn't wired up yet (or hiccups), the user still
            // gets their AI analysis rather than a failed request. See the
            // recommendations for hardening this later.
            try
            {
                await using var saveStream = file.OpenReadStream();
                var stored = await _fileStorage.SaveAsync(saveStream, file.FileName, ct);

                var idOrg = GetCurrentOrgId();
                var idUser = GetCurrentUserId();
                var analyzedOn = DateTime.UtcNow;

                var idAttachment = await _attachmentRepo.AddAsync(new AttachmentRecordInput
                {
                    IdOrg = idOrg,
                    CreatedBy = idUser,
                    FileName = file.FileName,
                    FilePath = stored.StoredFileName,
                    FileType = parsed.FileType,
                    FileSize = file.Length,
                    AttachmentSummary = parsed.Summary,
                    AttachmentDescription = parsed.Description,
                    AnalyzedOn = analyzedOn,
                    AnalysisStatus = "Completed",
                }, ct);

                parsed.Id = idAttachment;
                parsed.FileSize = FormatFileSize(file.Length);
                parsed.AnalyzedOn = analyzedOn;
                parsed.PreviewUrl = $"/api/attachment/file/{idAttachment}";
                parsed.DownloadUrl = $"/api/attachment/file/{idAttachment}?download=true";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist attachment {FileName} after analysis", file.FileName);
            }

            return Ok(parsed);

        }
        catch (JsonException)
        {
            return StatusCode(502, new { message = "Model did not return valid JSON for this attachment." });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { message = ex.Message });
        }
    }

    [HttpGet("list")]
    [MenuPermission("view", "attachments")]
    public async Task<ActionResult<IEnumerable<AttachmentListItem>>> List(CancellationToken ct)
    {
        var records = await _attachmentRepo.ListAsync(idAttachment: null, ct);
        return Ok(records.Where(record => record.IdOrg == GetCurrentOrgId()).Select(ToListItem));
    }

    [HttpGet("file/{id:int}")]
    [MenuPermission("view", "attachments")]
    public async Task<IActionResult> GetFile(int id, [FromQuery] bool download = false, CancellationToken ct = default)
    {
        var records = await _attachmentRepo.ListAsync(id, ct);
        var record = records.FirstOrDefault(record => record.IdOrg == GetCurrentOrgId());
        if (record is null || string.IsNullOrWhiteSpace(record.FilePath) || !_fileStorage.Exists(record.FilePath))
            return NotFound();

        var physicalPath = _fileStorage.GetPhysicalPath(record.FilePath);
        var contentType = GetContentType(record.FileType);

        return download
            ? PhysicalFile(physicalPath, contentType, record.FileName)
            : PhysicalFile(physicalPath, contentType);
    }

    [HttpDelete("{id:int}")]
    [MenuPermission("delete", "attachments")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var idOrg = GetCurrentOrgId();
        var records = await _attachmentRepo.ListAsync(id, ct);
        var record = records.FirstOrDefault(record => record.IdOrg == GetCurrentOrgId());
        if (record is null) return NotFound();

        try
        {
            await _attachmentRepo.DeleteAsync(id, idOrg, ct);
        }
        catch (Exception ex)
        {
            // attachment_analyzer_delete THROWs 50021 when the id doesn't
            // belong to this org — treat that as a 404, not a 500.
            _logger.LogWarning(ex, "Failed to delete attachment {Id}", id);
            return NotFound(new { message = "Attachment not found or does not belong to this organization." });
        }

        if (!string.IsNullOrWhiteSpace(record.FilePath))
        {
            _fileStorage.Delete(record.FilePath);
        }

        return Ok(new { id_attachment = id, operation = "DELETE" });
    }   
}
