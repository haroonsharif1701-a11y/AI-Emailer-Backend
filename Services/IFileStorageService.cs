namespace AiEmailer.Api.Services;

/// <summary>
/// Everything that knows "where files physically live" is isolated behind
/// this interface. Swapping LocalFileStorageService for an Azure Blob /
/// cloud implementation later is a one-file change — AttachmentController
/// and the repository never need to know the difference.
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Saves the stream under wwwroot/attachments (for now) as
    /// "{yyyyMMddHHmmssfff}_{originalFileName}" so same-name uploads never
    /// collide. Returns the stored (on-disk) name — save this in file_path —
    /// and a web-relative path.
    /// </summary>
    Task<StoredFile> SaveAsync(Stream content, string originalFileName, CancellationToken ct = default);
        
    /// <summary>Physical path on disk for a previously stored file name.</summary>
    string GetPhysicalPath(string storedFileName);

    /// <summary>
    /// Defensive helper: strips the "{yyyyMMddHHmmssfff}_" prefix back off.
    /// Not needed in the normal flow (file_name column already holds the
    /// clean original name), but useful if file_path is ever the only thing
    /// on hand.
    /// </summary>
    string GetOriginalFileName(string storedFileName);

    bool Exists(string storedFileName);

    void Delete(string storedFileName);
}

/// <param name="StoredFileName">On-disk name, e.g. "20260905153012123_Invoice.pdf" — goes into ekey_attachment_analyzer.file_path.</param>
/// <param name="RelativeUrl">e.g. "/attachments/20260905153012123_Invoice.pdf".</param>
public record StoredFile(string StoredFileName, string RelativeUrl);