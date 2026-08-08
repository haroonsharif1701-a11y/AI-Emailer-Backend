namespace AiEmailer.Api.Services;

/// <summary>
/// Extracts plain text from an uploaded file so it can be handed to
/// ILlmProvider for summarization/threat analysis. Same reasoning as
/// ILlmProvider: controllers depend on this interface only, so adding a new
/// file type (or swapping in a smarter extractor later) never touches
/// AttachmentController.
/// </summary>
public interface IAttachmentTextExtractor
{
    /// <summary>
    /// Throws NotSupportedException for file types that aren't handled yet
    /// (currently: images, since real OCR needs Tesseract + trained data
    /// files, a separate setup this doesn't include).
    /// </summary>
    Task<string> ExtractTextAsync(Stream fileStream, string fileName);
}
